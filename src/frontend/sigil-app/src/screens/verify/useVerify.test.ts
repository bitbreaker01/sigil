// Verify logic tests: the certificate parser (pure), the mock contract (both verification modes),
// and the useVerify hook driven with a real File through the EXACT app path
// (File → readFile → sha256Hex → VerifyDocument). Proves the verdict green/red/not-found.

import { describe, it, expect } from 'vitest';
import { renderHook, waitFor, act } from '@testing-library/react';
import { buildCertificate, useVerify } from './useVerify';
import { MockSigilApi } from '../../api/mock';
import { sigilApi } from '../../api';
import { base64ToBytes, sha256Hex } from '../../api/binaries';
import type { VerifyMetadata } from '../../api/contracts';

const TX = 'mock-000000000003'; // any seeded tx serves SAMPLE_PDF (seedTx sets no per-tx doc)

/** The hash the frontend computes for the document a tx serves — same path as verifyFile. */
async function servedHash(api: MockSigilApi, txId = TX): Promise<string> {
  const b64 = await api.getDocumentContent({ Target: txId, DocumentType: 'final' });
  return sha256Hex((await base64ToBytes(b64)).buffer);
}

/** The id of the seeded COMPLETED transaction (Employment Offer). */
async function completedTxId(api: MockSigilApi): Promise<string> {
  const requests = await api.myRequests();
  const completed = requests.find((t) => t.state === 159460004);
  if (!completed) throw new Error('no completed tx seeded');
  return completed.id;
}

function metaJson(over: Partial<VerifyMetadata> = {}): string {
  return JSON.stringify({
    found: true,
    ledgerNumber: 'SIGIL-2026-000042',
    sealedOnUtc: '2026-07-16T10:00:00Z',
    finalHashHex: 'A'.repeat(64),
    tsaStatus: 'sealed',
    historyIntact: true,
    signerSummary: JSON.stringify({
      signers: [{ name: 'Ana', email: 'ana@x.com', signedOnUtc: '2026-07-16T09:00:00Z' }],
      routing: 'parallel',
      completedOnUtc: '2026-07-16T10:00:00Z',
      tsa: { status: 'sealed' },
    }),
    ...over,
  } satisfies VerifyMetadata);
}

describe('buildCertificate', () => {
  it('parses metadata and signers when the document is intact', () => {
    const c = buildCertificate(metaJson(), true, true, 'dG9rZW4=');
    expect(c.found).toBe(true);
    expect(c.isIntact).toBe(true);
    expect(c.ledgerNumber).toBe('SIGIL-2026-000042');
    expect(c.tsaStatus).toBe('sealed');
    expect(c.historyIntact).toBe(true);
    expect(c.signers).toHaveLength(1);
    expect(c.signers[0]?.name).toBe('Ana');
    expect(c.tokenBase64).toBe('dG9rZW4=');
  });

  it('marks altered when isIntact is false', () => {
    const c = buildCertificate(metaJson(), true, false, undefined);
    expect(c.isIntact).toBe(false);
    expect(c.tokenBase64).toBeUndefined();
  });

  it('does not set isIntact when it is undefined (certificate only via deep link)', () => {
    const c = buildCertificate(metaJson(), true, undefined, undefined);
    expect(c.isIntact).toBeUndefined();
    expect('isIntact' in c).toBe(false);
  });

  it('degrades gracefully if the metadata is not JSON', () => {
    const c = buildCertificate('not-json', false, undefined, undefined);
    expect(c.found).toBe(false);
    expect(c.signers).toEqual([]);
  });

  it('leaves signers empty if signerSummary is corrupt', () => {
    const c = buildCertificate(metaJson({ signerSummary: '{{broken' }), true, true, undefined);
    expect(c.signers).toEqual([]);
  });
});

describe('getDocumentContent — original vs signed versions', () => {
  it('serves DIFFERENT bytes for content (original) and final (signed)', async () => {
    const api = new MockSigilApi();
    const txId = await completedTxId(api);
    const content = await api.getDocumentContent({ Target: txId, DocumentType: 'content' });
    const final = await api.getDocumentContent({ Target: txId, DocumentType: 'final' });
    expect(content).not.toBe(final); // the two versions are distinguishable in the viewer
  });

  it('verify targets the FINAL (signed) document: uploading the ORIGINAL does NOT verify', async () => {
    const api = new MockSigilApi();
    const txId = await completedTxId(api);
    const originalHash = await sha256Hex((await base64ToBytes(
      await api.getDocumentContent({ Target: txId, DocumentType: 'content' }))).buffer);
    const out = await api.verifyDocument({ TransactionId: txId, Sha256Hash: originalHash });
    expect(out.IsIntact).toBe(false); // the original is not the sealed document
  });
});

describe('verifyDocument — mode B (by transaction id: QR / detail)', () => {
  it('IsIntact=true when the uploaded hash matches the served document (GREEN)', async () => {
    const api = new MockSigilApi();
    const hash = await servedHash(api);
    const out = await api.verifyDocument({ TransactionId: TX, Sha256Hash: hash });
    expect(out.Found).toBe(true);
    expect(out.IsIntact).toBe(true);
    expect((JSON.parse(out.MetadataJson) as VerifyMetadata).finalHashHex).toBe(hash);
  });

  it('IsIntact=false for any other file (RED)', async () => {
    const api = new MockSigilApi();
    const hash = await servedHash(api);
    const altered = (hash[0] === 'A' ? 'B' : 'A') + hash.slice(1);
    const out = await api.verifyDocument({ TransactionId: TX, Sha256Hash: altered });
    expect(out.IsIntact).toBe(false);
  });

  it('is case-insensitive on the incoming hash', async () => {
    const api = new MockSigilApi();
    const hash = await servedHash(api);
    const out = await api.verifyDocument({ TransactionId: TX, Sha256Hash: hash.toLowerCase() });
    expect(out.IsIntact).toBe(true);
  });

  it('certificate only (no verdict) when no file hash is provided', async () => {
    const api = new MockSigilApi();
    const out = await api.verifyDocument({ TransactionId: TX });
    expect(out.Found).toBe(true);
    expect(out.IsIntact).toBeUndefined();
  });
});

describe('verifyDocument — mode A (by hash: drop any sealed PDF, no txId)', () => {
  it('finds the sealed record by hash and reports authentic & intact (GREEN)', async () => {
    const api = new MockSigilApi();
    const hash = await servedHash(api, await completedTxId(api));
    const out = await api.verifyDocument({ Sha256Hash: hash }); // no TransactionId
    expect(out.Found).toBe(true);
    expect(out.IsIntact).toBe(true);
    expect((JSON.parse(out.MetadataJson) as VerifyMetadata).finalHashHex).toBe(hash);
  });

  it('returns Found=false when the hash is not in the ledger (unknown / altered file)', async () => {
    const api = new MockSigilApi();
    const out = await api.verifyDocument({ Sha256Hash: 'F'.repeat(64) });
    expect(out.Found).toBe(false);
    expect(out.IsIntact).toBeUndefined();
  });
});

describe('useVerify (hook, real File through the app path)', () => {
  it('verifies by txId on mount (deep link) → certificate without file verdict', async () => {
    const { result } = renderHook(() => useVerify(TX));
    await waitFor(() => expect(result.current.state.phase).toBe('result'));
    const st = result.current.state as { phase: 'result'; certificate: { found: boolean; isIntact?: boolean } };
    expect(st.certificate.found).toBe(true);
    expect(st.certificate.isIntact).toBeUndefined();
  });

  it('GREEN when the exact sealed document is uploaded (mode B)', async () => {
    const b64 = await sigilApi.getDocumentContent({ Target: TX, DocumentType: 'final' });
    const file = new File([await base64ToBytes(b64)], 'sealed.pdf', { type: 'application/pdf' });
    const { result } = renderHook(() => useVerify(TX));
    await waitFor(() => expect(result.current.state.phase).toBe('result'));

    await act(async () => { await result.current.verifyFile(file, TX); });

    const st = result.current.state as { phase: 'result'; certificate: { isIntact?: boolean } };
    expect(st.certificate.isIntact).toBe(true);
  });

  it('RED when a different (altered) file is uploaded (mode B)', async () => {
    const b64 = await sigilApi.getDocumentContent({ Target: TX, DocumentType: 'final' });
    const tampered = new Uint8Array(await base64ToBytes(b64));
    tampered[tampered.length - 1] ^= 0xff;
    const file = new File([tampered], 'tampered.pdf', { type: 'application/pdf' });
    const { result } = renderHook(() => useVerify(TX));
    await waitFor(() => expect(result.current.state.phase).toBe('result'));

    await act(async () => { await result.current.verifyFile(file, TX); });

    const st = result.current.state as { phase: 'result'; certificate: { isIntact?: boolean } };
    expect(st.certificate.isIntact).toBe(false);
  });

  it('with NO txId, hashes the file and looks it up by hash → GREEN (mode A, the menu path)', async () => {
    const b64 = await sigilApi.getDocumentContent({ Target: TX, DocumentType: 'final' });
    const file = new File([await base64ToBytes(b64)], 'sealed.pdf', { type: 'application/pdf' });
    const { result } = renderHook(() => useVerify()); // no deep-link txId (nav "Verify")

    await act(async () => { await result.current.verifyFile(file, undefined); });

    const st = result.current.state as { phase: 'result'; certificate: { found: boolean; isIntact?: boolean } };
    expect(st.phase).toBe('result');
    expect(st.certificate.found).toBe(true);
    expect(st.certificate.isIntact).toBe(true);
  });

  it('with NO txId, an unknown file → not found (mode A)', async () => {
    const file = new File([new Uint8Array([9, 8, 7, 6, 5])], 'random.pdf', { type: 'application/pdf' });
    const { result } = renderHook(() => useVerify());

    await act(async () => { await result.current.verifyFile(file, undefined); });

    const st = result.current.state as { phase: 'result'; certificate: { found: boolean } };
    expect(st.phase).toBe('result');
    expect(st.certificate.found).toBe(false);
  });
});

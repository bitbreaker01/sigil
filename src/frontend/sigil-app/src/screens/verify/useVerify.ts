// Verify logic. The file NEVER leaves the browser:
// it's hashed locally with Web Crypto and only the hash (64 hex) goes to VerifyDocument. Supports
// two inputs: a file (drag&drop / picker) or a deep-link txId (certificate only).

import { useCallback, useEffect, useState } from 'react';
import { sigilApi } from '../../api';
import { sha256Hex, readFile } from '../../api/binaries';
import type { VerifyDocumentInput, VerifyMetadata, SignerSummary } from '../../api/contracts';

export interface Certificate {
  found: boolean;
  isIntact?: boolean; // present only if a file was verified
  ledgerNumber?: string;
  sealedOnUtc?: string;
  finalHashHex?: string;
  tsaStatus?: 'sealed' | 'pending' | 'none';
  historyIntact?: boolean;
  signers: { name: string; email: string; signedOnUtc: string | null }[];
  tokenBase64?: string;
}

export type VerifyState =
  | { phase: 'initial' }
  | { phase: 'computing' }
  | { phase: 'result'; certificate: Certificate }
  | { phase: 'error'; message: string };

/** Parses the metadata + signerSummary from VerifyDocument's output into the certificate view. */
export function buildCertificate(
  metadataJson: string,
  found: boolean,
  isIntact: boolean | undefined,
  tokenBase64: string | undefined,
): Certificate {
  let meta: VerifyMetadata;
  try {
    meta = JSON.parse(metadataJson) as VerifyMetadata;
  } catch {
    meta = { found };
  }
  let signers: Certificate['signers'] = [];
  if (meta.signerSummary) {
    try {
      signers = (JSON.parse(meta.signerSummary) as SignerSummary).signers ?? [];
    } catch {
      signers = [];
    }
  }
  const c: Certificate = { found, signers };
  if (isIntact !== undefined) c.isIntact = isIntact;
  if (meta.ledgerNumber) c.ledgerNumber = meta.ledgerNumber;
  if (meta.sealedOnUtc) c.sealedOnUtc = meta.sealedOnUtc;
  if (meta.finalHashHex) c.finalHashHex = meta.finalHashHex;
  if (meta.tsaStatus) c.tsaStatus = meta.tsaStatus;
  if (meta.historyIntact !== undefined) c.historyIntact = meta.historyIntact;
  if (tokenBase64) c.tokenBase64 = tokenBase64;
  return c;
}

export function useVerify(initialTxId?: string) {
  const [state, setState] = useState<VerifyState>({ phase: 'initial' });

  const verifyByTxId = useCallback(async (txId: string) => {
    setState({ phase: 'computing' });
    try {
      const r = await sigilApi.verifyDocument({ TransactionId: txId });
      setState({ phase: 'result', certificate: buildCertificate(r.MetadataJson, r.Found, r.IsIntact, r.TsaTokenBase64) });
    } catch {
      setState({ phase: 'error', message: 'common.genericError' });
    }
  }, []);

  const verifyFile = useCallback(async (file: File, txId?: string) => {
    // The file is hashed locally (it never leaves the browser) and only the 64-hex hash travels.
    // With a txId (QR / detail) we verify against THAT record; without it we let the backend find
    // the sealed record by its hash (ledger lookup) — drop any sealed PDF and get a verdict.
    setState({ phase: 'computing' });
    try {
      const hash = await sha256Hex(await readFile(file));
      const input: VerifyDocumentInput = txId ? { TransactionId: txId, Sha256Hash: hash } : { Sha256Hash: hash };
      const r = await sigilApi.verifyDocument(input);
      setState({ phase: 'result', certificate: buildCertificate(r.MetadataJson, r.Found, r.IsIntact, r.TsaTokenBase64) });
    } catch {
      setState({ phase: 'error', message: 'common.genericError' });
    }
  }, []);

  useEffect(() => {
    if (initialTxId) void verifyByTxId(initialTxId);
  }, [initialTxId, verifyByTxId]);

  return { state, verifyByTxId, verifyFile, initialTxId };
}

// M11 (doc 11 §4) — the wizard validations are a MIRROR of doc 04 §3.4, the coordinate math
// is the shared contract (doc 04 §6.1), and binaries decode without freezing the thread.
// The same edge cases as the backend, on the client side.

import { describe, it, expect } from 'vitest';
import {
  validateParticipants,
  validateZones,
  participantsWithoutZone,
  validateHeader,
  validatePdf,
} from './validations';
import { pxToPercent, percentToPx } from './coordinates';
import { base64ToBytes, bytesToBase64 } from './binaries';

describe('validateParticipants — mirror doc 04 §3.4', () => {
  it('parallel without order is valid', () => {
    expect(validateParticipants([{ userId: 'a' }, { userId: 'b' }], 'parallel', 20).ok).toBe(true);
  });
  it('sequential 1..N consecutive is valid', () => {
    expect(validateParticipants([{ userId: 'a', order: 1 }, { userId: 'b', order: 2 }], 'sequential', 20).ok).toBe(true);
  });
  it('duplicates are rejected', () => {
    const r = validateParticipants([{ userId: 'a' }, { userId: 'a' }], 'parallel', 20);
    expect(r.ok).toBe(false);
    expect(r.errors).toContain('validation.duplicateParticipants');
  });
  it('order with gaps is rejected', () => {
    const r = validateParticipants([{ userId: 'a', order: 1 }, { userId: 'b', order: 3 }], 'sequential', 20);
    expect(r.errors).toContain('validation.orderWithGaps');
  });
  it('sequential without order is rejected', () => {
    expect(validateParticipants([{ userId: 'a' }], 'sequential', 20).errors).toContain('validation.sequentialWithoutOrder');
  });
  it('parallel with order is rejected', () => {
    expect(validateParticipants([{ userId: 'a', order: 1 }], 'parallel', 20).errors).toContain('validation.parallelWithOrder');
  });
  it('over the maximum is rejected', () => {
    expect(validateParticipants([{ userId: 'a' }, { userId: 'b' }, { userId: 'c' }], 'parallel', 2).errors).toContain('validation.maxParticipants');
  });
});

describe('validateZones — mirror doc 04 §3.4', () => {
  const parts = new Set(['a', 'b']);
  it('valid zone passes', () => {
    expect(validateZones([{ userId: 'a', page: 1, x: 10, y: 10, w: 20, h: 8 }], parts, 3).ok).toBe(true);
  });
  it('non-participant zone is rejected', () => {
    expect(validateZones([{ userId: 'z', page: 1, x: 10, y: 10, w: 20, h: 8 }], parts, 3).errors).toContain('validation.orphanZone');
  });
  it('nonexistent page is rejected', () => {
    expect(validateZones([{ userId: 'a', page: 5, x: 10, y: 10, w: 20, h: 8 }], parts, 3).errors).toContain('validation.zonePage');
  });
  it('out-of-range coordinates are rejected', () => {
    expect(validateZones([{ userId: 'a', page: 1, x: -1, y: 10, w: 20, h: 8 }], parts, 3).errors).toContain('validation.zoneOutOfRange');
  });
  it('a zone that overflows the page is rejected', () => {
    expect(validateZones([{ userId: 'a', page: 1, x: 90, y: 10, w: 20, h: 8 }], parts, 3).errors).toContain('validation.zoneOverflow');
  });
});

describe('participantsWithoutZone — RF-28', () => {
  it('lists who is missing a zone', () => {
    const missing = participantsWithoutZone(
      [{ userId: 'a' }, { userId: 'b' }, { userId: 'c' }],
      [{ userId: 'a', page: 1, x: 1, y: 1, w: 1, h: 1 }, { userId: 'c', page: 1, x: 1, y: 1, w: 1, h: 1 }],
    );
    expect(missing).toEqual(['b']);
  });
});

describe('validateHeader', () => {
  it('missing title is rejected', () => {
    expect(validateHeader('', undefined, undefined).errors).toContain('validation.titleRequired');
  });
  it('title > 200 is rejected', () => {
    expect(validateHeader('x'.repeat(201), undefined, undefined).errors).toContain('validation.titleTooLong');
  });
  it('non-positive expiration is rejected', () => {
    expect(validateHeader('Doc', undefined, 0).errors).toContain('validation.invalidExpiration');
  });
  it('message > 2000 is rejected', () => {
    expect(validateHeader('Doc', 'm'.repeat(2001), undefined).errors).toContain('validation.messageTooLong');
  });
});

describe('validatePdf', () => {
  it('non-PDF is rejected', () => {
    const f = new File(['x'], 'foto.png', { type: 'image/png' });
    expect(validatePdf(f, 100).errors).toContain('validation.pdfExtension');
  });
  it('over the size is rejected', () => {
    const f = new File([new Uint8Array(200 * 1024)], 'doc.pdf', { type: 'application/pdf' });
    expect(validatePdf(f, 100).errors).toContain('validation.pdfSize');
  });
});

describe('coordinates — contract doc 04 §6.1 (roundtrip)', () => {
  it('px → % → px returns to the same place', () => {
    const pct = pxToPercent({ xPx: 100, yPx: 50, wPx: 200, hPx: 80 }, 400, 800);
    expect(pct).toEqual({ x: 25, y: 6.25, w: 50, h: 10 });
    const px = percentToPx(pct, 400, 800);
    expect(px).toEqual({ xPx: 100, yPx: 50, wPx: 200, hPx: 80 });
  });
  it('the result is independent of zoom (% equal at a different canvas size)', () => {
    const a = pxToPercent({ xPx: 100, yPx: 50, wPx: 200, hPx: 80 }, 400, 800);
    const b = pxToPercent({ xPx: 200, yPx: 100, wPx: 400, hPx: 160 }, 800, 1600); // 2x zoom
    expect(b).toEqual(a);
  });
  it('clamps a zone that goes off the canvas', () => {
    const pct = pxToPercent({ xPx: 380, yPx: 10, wPx: 100, hPx: 10 }, 400, 800);
    expect(pct.x + pct.w).toBeLessThanOrEqual(100);
  });
});

describe('binaries — roundtrip without corruption', () => {
  it('bytes → base64 → bytes is identical', async () => {
    const bytes = new Uint8Array([0, 1, 2, 253, 254, 255, 65, 66, 67]);
    const b64 = await bytesToBase64(bytes);
    const back = await base64ToBytes(b64);
    expect([...back]).toEqual([...bytes]);
  });
});

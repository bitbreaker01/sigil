// Tests of the zone editor geometry, including the ASPECT-RATIO LOCK: every zone must be visually
// 3:1 (the Master Signature ratio, 600×200, env_SignatureImageSpec) so the sealing engine — which
// STRETCHES the signature to fill the zone — never distorts it. Also covers the move/resize edge
// behavior the earlier review caught.

import { describe, it, expect } from 'vitest';
import {
  makeZone, moveZone, resizeZone, setZoneField, heightPctForWidth,
  MIN_ZONE_PCT, SIGNATURE_ASPECT, type PageSize,
} from './zoneGeometry';
import type { RectPct } from '../api/coordinates';

// A square-ish rendered page (600×800 px). On it, a visually 3:1 zone has w:h in % of
// wPct*600 : hPct*800 = 3:1 → hPct = wPct * 600 / (3*800) = wPct/4.
const PAGE: PageSize = { width: 600, height: 800 };

/** Assert the zone's VISUAL pixel ratio equals the signature ratio (within rounding). */
function expectRatio(r: RectPct, page: PageSize) {
  const wpx = (r.w / 100) * page.width;
  const hpx = (r.h / 100) * page.height;
  expect(wpx / hpx).toBeCloseTo(SIGNATURE_ASPECT, 2);
}

describe('makeZone (ratio-locked creation)', () => {
  it('derives height from width to be visually 3:1', () => {
    const z = makeZone(10, 10, 40, PAGE);
    expect(z.w).toBe(40);
    expect(z.h).toBeCloseTo(10, 4); // 40 * 600/(3*800) = 10
    expectRatio(z, PAGE);
  });
  it('caps width so the derived height still fits the page', () => {
    const z = makeZone(0, 80, 100, PAGE); // only 20% height room → width capped
    expect(z.y + z.h).toBeLessThanOrEqual(100.001);
    expectRatio(z, PAGE);
  });
});

describe('moveZone', () => {
  it('shifts origin, keeps size, stops at the edge without deforming', () => {
    const box: RectPct = { x: 40, y: 40, w: 40, h: 10 };
    expect(moveZone(box, 1000, 1000)).toEqual({ x: 60, y: 90, w: 40, h: 10 });
  });
});

describe('resizeZone (one DOF, ratio preserved)', () => {
  it('grows width and derives height to keep 3:1', () => {
    const box = makeZone(10, 10, 30, PAGE);
    const r = resizeZone(box, 20, 0, PAGE);
    expect(r.w).toBeCloseTo(50, 4);
    expectRatio(r, PAGE);
  });
  it('never shrinks below MIN and never overflows the page', () => {
    const box = makeZone(10, 10, 30, PAGE);
    expect(resizeZone(box, -1000, 0, PAGE).w).toBe(MIN_ZONE_PCT);
    const big = resizeZone(box, 1000, 0, PAGE);
    expect(big.x + big.w).toBeLessThanOrEqual(100.001);
    expect(big.y + big.h).toBeLessThanOrEqual(100.001);
    expectRatio(big, PAGE);
  });
});

describe('setZoneField (numeric inputs)', () => {
  const box = makeZone(10, 10, 40, PAGE); // h ≈ 10

  it('x/y only move, keeping size and staying in-page', () => {
    expect(setZoneField(box, 'x', 95, PAGE).x).toBe(60); // 100 - w(40)
    expect(setZoneField(box, 'y', 99, PAGE).y).toBeCloseTo(90, 4); // 100 - h(10)
    expect(setZoneField(box, 'x', 20, PAGE).w).toBe(box.w); // size unchanged
  });
  it('editing width re-derives height to keep the ratio', () => {
    const r = setZoneField(box, 'w', 60, PAGE);
    expect(r.w).toBe(60);
    expectRatio(r, PAGE);
  });
  it('caps width so the derived height fits, and floors at MIN', () => {
    const r = setZoneField(makeZone(0, 90, 10, PAGE), 'w', 100, PAGE);
    expect(r.y + r.h).toBeLessThanOrEqual(100.001);
    expect(setZoneField(box, 'w', -5, PAGE).w).toBe(MIN_ZONE_PCT);
  });
  it('treats NaN as 0', () => {
    expect(setZoneField(box, 'x', Number.NaN, PAGE).x).toBe(0);
  });
});

describe('heightPctForWidth', () => {
  it('is proportional to the page aspect', () => {
    expect(heightPctForWidth(30, { width: 300, height: 300 })).toBeCloseTo(10, 4); // square page: h = w/3
  });
});

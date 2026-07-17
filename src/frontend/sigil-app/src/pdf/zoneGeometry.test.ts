// Tests of the zone editor geometry — the two failure modes the adversarial review caught:
// moving must not deform at the edge, and the numeric inputs must keep x+w / y+h ≤ 100.

import { describe, it, expect } from 'vitest';
import { moveZone, resizeZone, clampCoord, MIN_ZONE_PCT } from './zoneGeometry';
import type { RectPct } from '../api/coordinates';

const box: RectPct = { x: 40, y: 40, w: 20, h: 10 };

describe('moveZone', () => {
  it('shifts the origin without changing size', () => {
    expect(moveZone(box, 10, 5)).toEqual({ x: 50, y: 45, w: 20, h: 10 });
  });
  it('stops at the right/bottom edge WITHOUT shrinking (the review bug)', () => {
    const r = moveZone(box, 1000, 1000);
    expect(r.w).toBe(20); // width preserved
    expect(r.h).toBe(10);
    expect(r.x).toBe(80); // 100 - w
    expect(r.y).toBe(90); // 100 - h
    expect(r.x + r.w).toBeLessThanOrEqual(100);
  });
  it('stops at the top/left edge', () => {
    expect(moveZone(box, -1000, -1000)).toEqual({ x: 0, y: 0, w: 20, h: 10 });
  });
});

describe('resizeZone', () => {
  it('grows from a fixed origin', () => {
    expect(resizeZone(box, 10, 5)).toEqual({ x: 40, y: 40, w: 30, h: 15 });
  });
  it('caps width/height so the box fits the page', () => {
    const r = resizeZone(box, 1000, 1000);
    expect(r.w).toBe(60); // 100 - x
    expect(r.h).toBe(60); // 100 - y
    expect(r.x + r.w).toBeLessThanOrEqual(100);
  });
  it('floors at MIN_ZONE_PCT (no zero-size zone)', () => {
    const r = resizeZone(box, -1000, -1000);
    expect(r.w).toBe(MIN_ZONE_PCT);
    expect(r.h).toBe(MIN_ZONE_PCT);
  });
});

describe('clampCoord (numeric inputs)', () => {
  it('clamps a single field to [0,100]', () => {
    expect(clampCoord(box, 'x', -5)).toBe(0);
    expect(clampCoord(box, 'x', 200)).toBe(80); // also capped by fit: 100 - w(20)
  });
  it('enforces x + w ≤ 100 when editing x', () => {
    expect(clampCoord(box, 'x', 95)).toBe(80); // 100 - w
  });
  it('enforces w ≤ 100 - x when editing w', () => {
    expect(clampCoord(box, 'w', 90)).toBe(60); // 100 - x
  });
  it('enforces y + h and h likewise', () => {
    expect(clampCoord(box, 'y', 99)).toBe(90); // 100 - h
    expect(clampCoord(box, 'h', 99)).toBe(60); // 100 - y
  });
  it('treats NaN as 0', () => {
    expect(clampCoord(box, 'x', Number.NaN)).toBe(0);
  });
});

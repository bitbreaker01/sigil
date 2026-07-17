// Pure geometry for the zone editor (doc 05 §6.3), extracted so the interaction math is testable
// without a browser. Everything is in the shared %-coordinate contract (0–100, top-left origin).
// Two failure modes the review caught live here now, locked by tests:
//   - MOVE must clamp the ORIGIN (never deform the box at the page edge).
//   - the numeric inputs must enforce x+w ≤ 100 / y+h ≤ 100 (the accessibility path).

import { clamp, round, type RectPct } from '../api/coordinates';

export const MIN_ZONE_PCT = 1; // a zone can't be smaller than 1% of the page

/** Move: shift the origin, keep w/h, and stop at the edge so the box stays whole. */
export function moveZone(orig: RectPct, dxPct: number, dyPct: number): RectPct {
  return {
    x: round(clamp(orig.x + dxPct, 0, 100 - orig.w)),
    y: round(clamp(orig.y + dyPct, 0, 100 - orig.h)),
    w: orig.w,
    h: orig.h,
  };
}

/** Resize: grow w/h from a fixed origin, floored at MIN and capped to fit the page. */
export function resizeZone(orig: RectPct, dxPct: number, dyPct: number): RectPct {
  return {
    x: orig.x,
    y: orig.y,
    w: round(clamp(orig.w + dxPct, MIN_ZONE_PCT, 100 - orig.x)),
    h: round(clamp(orig.h + dyPct, MIN_ZONE_PCT, 100 - orig.y)),
  };
}

/** Numeric input: clamp one field to [0,100] AND enforce fit against its sibling dimension. */
export function clampCoord(rect: RectPct, key: 'x' | 'y' | 'w' | 'h', value: number): number {
  let n = clamp(Number.isFinite(value) ? value : 0, 0, 100);
  if (key === 'x') n = Math.min(n, 100 - rect.w);
  else if (key === 'y') n = Math.min(n, 100 - rect.h);
  else if (key === 'w') n = Math.min(n, 100 - rect.x);
  else if (key === 'h') n = Math.min(n, 100 - rect.y);
  return round(Math.max(0, n));
}

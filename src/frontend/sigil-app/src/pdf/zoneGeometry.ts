// Pure geometry for the zone editor (doc 05 §6.3), testable without a browser. Everything is in
// the shared %-coordinate contract (0–100, top-left origin).
//
// ASPECT-RATIO LOCK (critical, not cosmetic): the sealing engine embeds the Master Signature by
// STRETCHING it to fill the zone — TransformacionDeCoordenadas.ParaZona builds cm(vw,0,0,vh,…),
// scaling X by the zone width and Y by the zone height independently. The normalized Master
// Signature is always a fixed 600×200 canvas (env_SignatureImageSpec, doc 04 §4) → 3:1. So a
// zone whose VISUAL aspect ratio isn't 3:1 would distort the signature. We therefore lock every
// zone to 3:1 *in rendered pixels* (pdf.js renders the visual CropBox, so pixel ratio == the PDF
// zone ratio). Zones vary in SIZE (one degree of freedom) but never in shape.

import { clamp, round, type RectPct } from '../api/coordinates';

export const MIN_ZONE_PCT = 2; // a zone can't be narrower than 2% of the page

/** The Master Signature aspect ratio (width/height). MUST match env_SignatureImageSpec
 *  (600×200 = 3, doc 04 §4). Single source of truth in the frontend, like domain/states. */
export const SIGNATURE_ASPECT = 3;

export interface PageSize {
  width: number; // rendered px (carries the page's VISUAL aspect)
  height: number;
}

/** Height% that makes a zone of the given width% look `ratio:1` on this page. */
export function heightPctForWidth(wPct: number, page: PageSize, ratio = SIGNATURE_ASPECT): number {
  if (page.width <= 0 || page.height <= 0) return 0; // degenerate page → no-op (avoids NaN/Infinity)
  return (wPct * page.width) / (ratio * page.height);
}

/** Inverse: width% for a given height%. */
export function widthPctForHeight(hPct: number, page: PageSize, ratio = SIGNATURE_ASPECT): number {
  if (page.width <= 0 || page.height <= 0) return 0;
  return (hPct * ratio * page.height) / page.width;
}

/** Largest width% that fits both the page width (100−x) and the derived height (100−y). */
export function maxWidthPct(x: number, y: number, page: PageSize, ratio = SIGNATURE_ASPECT): number {
  return Math.max(MIN_ZONE_PCT, Math.min(100 - x, widthPctForHeight(100 - y, page, ratio)));
}

/** Build a ratio-locked zone from a top-left origin + desired width%, clamped to fit. */
export function makeZone(xPct: number, yPct: number, wPct: number, page: PageSize, ratio = SIGNATURE_ASPECT): RectPct {
  const x = clamp(xPct, 0, 100 - MIN_ZONE_PCT);
  const y = clamp(yPct, 0, 100 - MIN_ZONE_PCT);
  const w = clamp(wPct, MIN_ZONE_PCT, maxWidthPct(x, y, page, ratio));
  return { x: round(x), y: round(y), w: round(w), h: round(heightPctForWidth(w, page, ratio)) };
}

/** Move: shift the origin, keep size, stop at the edge so the box stays whole (never deforms). */
export function moveZone(orig: RectPct, dxPct: number, dyPct: number): RectPct {
  return {
    x: round(clamp(orig.x + dxPct, 0, 100 - orig.w)),
    y: round(clamp(orig.y + dyPct, 0, 100 - orig.h)),
    w: orig.w,
    h: orig.h,
  };
}

/** Resize: one degree of freedom — the width delta drives the size; height follows the ratio. */
export function resizeZone(orig: RectPct, dxPct: number, dyPct: number, page: PageSize, ratio = SIGNATURE_ASPECT): RectPct {
  // Use whichever handle motion is larger so dragging feels natural, mapped onto width.
  const widthDelta = Math.abs(dxPct) >= Math.abs(dyPct) ? dxPct : widthPctForHeight(dyPct, page, ratio);
  const w = clamp(orig.w + widthDelta, MIN_ZONE_PCT, maxWidthPct(orig.x, orig.y, page, ratio));
  return { x: orig.x, y: orig.y, w: round(w), h: round(heightPctForWidth(w, page, ratio)) };
}

/** Numeric inputs (accessibility, §6.3): x/y move; width resizes (height is derived, read-only). */
export function setZoneField(rect: RectPct, key: 'x' | 'y' | 'w', value: number, page: PageSize, ratio = SIGNATURE_ASPECT): RectPct {
  const v = Number.isFinite(value) ? value : 0;
  if (key === 'x') return { ...rect, x: round(clamp(v, 0, 100 - rect.w)) };
  if (key === 'y') return { ...rect, y: round(clamp(v, 0, 100 - rect.h)) };
  const w = clamp(v, MIN_ZONE_PCT, maxWidthPct(rect.x, rect.y, page, ratio));
  return { ...rect, w: round(w), h: round(heightPctForWidth(w, page, ratio)) };
}

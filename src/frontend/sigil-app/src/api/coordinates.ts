// Coordinate contract for the zone editor (doc 05 §6.3, shared with doc 04 §6.1):
// zones are stored in % of the page's VISIBLE area (CropBox), origin TOP-LEFT,
// VISUAL orientation — INDEPENDENT of zoom. pdf.js renders in visual orientation
// (applies /Rotate and CropBox), so the frontend measures on the rendered canvas and
// converts to %; the backend compensates for rotation when embedding (validated by spike).

export interface RectPx {
  xPx: number;
  yPx: number;
  wPx: number;
  hPx: number;
}

export interface RectPct {
  x: number; // 0–100
  y: number;
  w: number;
  h: number;
}

/** Pixels on the rendered canvas → % of the visible area. Clamped to [0,100] and to fit. */
export function pxToPercent(rect: RectPx, widthPx: number, heightPx: number): RectPct {
  const x = clamp((rect.xPx / widthPx) * 100, 0, 100);
  const y = clamp((rect.yPx / heightPx) * 100, 0, 100);
  const w = clamp((rect.wPx / widthPx) * 100, 0, 100 - x);
  const h = clamp((rect.hPx / heightPx) * 100, 0, 100 - y);
  return { x: round(x), y: round(y), w: round(w), h: round(h) };
}

/** % → pixels on the current canvas (to draw the overlay at any zoom). */
export function percentToPx(rect: RectPct, widthPx: number, heightPx: number): RectPx {
  return {
    xPx: (rect.x / 100) * widthPx,
    yPx: (rect.y / 100) * heightPx,
    wPx: (rect.w / 100) * widthPx,
    hPx: (rect.h / 100) * heightPx,
  };
}

export function clamp(v: number, min: number, max: number): number {
  return Math.max(min, Math.min(max, v));
}

export function round(v: number): number {
  return Math.round(v * 10000) / 10000; // precision 4 (doc 03 §4.3)
}

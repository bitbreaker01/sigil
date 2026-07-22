// Client-side image transform for the signature editor: takes the source image + the crop area
// (from react-easy-crop, relative to the ROTATED image's bounding box), a rotation, and H/V flips,
// and returns the edited PNG as raw base64 (no data-URL prefix) — ready for validateMasterSignature.

import type { Area } from 'react-easy-crop';

function loadImage(src: string): Promise<HTMLImageElement> {
  return new Promise((resolve, reject) => {
    const img = new Image();
    img.onload = () => resolve(img);
    img.onerror = () => reject(new Error('image load failed'));
    img.src = src;
  });
}

const rad = (deg: number): number => (deg * Math.PI) / 180;

export async function getEditedPng(
  source: string,
  crop: Area,
  rotation = 0,
  flipH = false,
  flipV = false,
): Promise<string> {
  const image = await loadImage(source);
  const rot = rad(rotation);

  // 1) Draw the rotated image into a canvas sized to its bounding box (so nothing is clipped).
  const bbW = Math.abs(Math.cos(rot) * image.width) + Math.abs(Math.sin(rot) * image.height);
  const bbH = Math.abs(Math.sin(rot) * image.width) + Math.abs(Math.cos(rot) * image.height);
  const rotated = document.createElement('canvas');
  rotated.width = Math.round(bbW);
  rotated.height = Math.round(bbH);
  const rctx = rotated.getContext('2d');
  if (!rctx) throw new Error('no 2d context');
  rctx.translate(rotated.width / 2, rotated.height / 2);
  rctx.rotate(rot);
  rctx.drawImage(image, -image.width / 2, -image.height / 2);

  // 2) Extract the crop region via drawImage (croppedAreaPixels is in this rotated space). Using
  // drawImage (not getImageData) is robust when the crop extends BEYOND the image — which happens
  // when the user zooms out below 1 to fit a near-square signature into the wide 3:1 box: the area
  // outside the image stays transparent (the signature ends up centered with transparent padding),
  // whereas getImageData with negative/oversized coords is quirky across browsers.
  const cw = Math.max(1, Math.round(crop.width));
  const ch = Math.max(1, Math.round(crop.height));
  const cropped = document.createElement('canvas');
  cropped.width = cw;
  cropped.height = ch;
  const cctx = cropped.getContext('2d');
  if (!cctx) throw new Error('no 2d context');
  cctx.drawImage(rotated, -Math.round(crop.x), -Math.round(crop.y));

  // 3) Output canvas — apply the H/V flip.
  const out = document.createElement('canvas');
  out.width = cw;
  out.height = ch;
  const octx = out.getContext('2d');
  if (!octx) throw new Error('no 2d context');
  octx.translate(flipH ? out.width : 0, flipV ? out.height : 0);
  octx.scale(flipH ? -1 : 1, flipV ? -1 : 1);
  octx.drawImage(cropped, 0, 0);

  return out.toDataURL('image/png').replace(/^data:image\/png;base64,/, '');
}

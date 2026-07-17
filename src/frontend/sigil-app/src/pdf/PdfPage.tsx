// Renders ONE PDF page to a canvas at a target CSS width. pdf.js applies /Rotate and uses the
// CropBox in its viewport, so the canvas shows the VISUAL orientation — exactly the coordinate
// contract the zone editor and backend share (doc 04 §6.1 / doc 05 §6.1). Reports the rendered
// pixel size so an overlay can map the shared %-coordinates to on-screen pixels.

import { useEffect, useRef } from 'react';
import { makeStyles, tokens } from '@fluentui/react-components';
import type { PdfDoc } from './pdfjs';

export interface RenderedSize {
  width: number;
  height: number;
}

const useStyles = makeStyles({
  // flexShrink:0 so the canvas keeps its intrinsic (possibly zoomed) size inside the scroll wrapper.
  wrap: { position: 'relative', lineHeight: 0, flexShrink: 0 },
  canvas: {
    display: 'block',
    boxShadow: tokens.shadow4,
    borderRadius: tokens.borderRadiusSmall,
  },
});

export function PdfPage(props: {
  doc: PdfDoc;
  pageNumber: number;
  width: number;
  onRendered?: (size: RenderedSize) => void;
  children?: React.ReactNode; // overlay (zones), positioned absolutely over the canvas
}): JSX.Element {
  const s = useStyles();
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const { doc, pageNumber, width, onRendered } = props;

  useEffect(() => {
    let cancelled = false;
    let task: { cancel: () => void; promise: Promise<void> } | undefined;
    void (async () => {
      const page = await doc.getPage(pageNumber);
      if (cancelled) return;
      const base = page.getViewport({ scale: 1 });
      const scale = width / base.width;
      const css = page.getViewport({ scale });                 // CSS/layout size (what the overlay maps to)
      const dpr = window.devicePixelRatio || 1;
      const hi = page.getViewport({ scale: scale * dpr });     // backing store at device resolution → sharp at zoom
      const cssW = Math.floor(css.width), cssH = Math.floor(css.height);
      const canvas = canvasRef.current;
      const ctx = canvas?.getContext('2d');
      if (!canvas || !ctx) return;
      canvas.width = Math.floor(hi.width);
      canvas.height = Math.floor(hi.height);
      canvas.style.width = `${cssW}px`;
      canvas.style.height = `${cssH}px`;
      task = page.render({ canvasContext: ctx, viewport: hi });
      try {
        await task.promise;
        // Report the CSS size — overlays are positioned in CSS pixels over the displayed canvas.
        if (!cancelled) onRendered?.({ width: cssW, height: cssH });
      } catch {
        // RenderingCancelledException when width/page changes mid-render — expected, ignore.
      }
    })();
    return () => {
      cancelled = true;
      task?.cancel();
    };
  }, [doc, pageNumber, width, onRendered]);

  return (
    <div className={s.wrap}>
      <canvas ref={canvasRef} className={s.canvas} />
      {props.children}
    </div>
  );
}

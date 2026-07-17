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
  wrap: { position: 'relative', lineHeight: 0 },
  canvas: {
    display: 'block',
    boxShadow: tokens.shadow4,
    borderRadius: tokens.borderRadiusSmall,
    maxWidth: '100%',
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
      const viewport = page.getViewport({ scale: width / base.width });
      const canvas = canvasRef.current;
      const ctx = canvas?.getContext('2d');
      if (!canvas || !ctx) return;
      canvas.width = Math.floor(viewport.width);
      canvas.height = Math.floor(viewport.height);
      task = page.render({ canvasContext: ctx, viewport });
      try {
        await task.promise;
        if (!cancelled) onRendered?.({ width: canvas.width, height: canvas.height });
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

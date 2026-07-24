// Loads a PDF (from base64) into a pdf.js document and exposes its page count. The document is
// destroyed on unmount / when the source changes — binaries never linger. The
// base64 is decoded in chunks with yields (binaries.base64ToBytes) so a 27 MB PDF doesn't freeze
// the main thread on mobile.

import { useEffect, useRef, useState } from 'react';
import { pdfjsLib, type PdfDoc } from './pdfjs';
import { base64ToBytes } from '../api/binaries';

export type PdfLoadState =
  | { phase: 'idle' }
  | { phase: 'loading' }
  | { phase: 'ready'; doc: PdfDoc; pageCount: number }
  | { phase: 'error' };

export function usePdfDocument(base64: string | undefined): PdfLoadState {
  const [state, setState] = useState<PdfLoadState>({ phase: 'idle' });
  const docRef = useRef<PdfDoc | undefined>(undefined);

  useEffect(() => {
    if (!base64) {
      setState({ phase: 'idle' });
      return;
    }
    let cancelled = false;
    setState({ phase: 'loading' });
    void (async () => {
      try {
        const bytes = await base64ToBytes(base64);
        const doc = await pdfjsLib.getDocument({ data: bytes }).promise;
        if (cancelled) {
          void doc.destroy();
          return;
        }
        docRef.current = doc;
        setState({ phase: 'ready', doc, pageCount: doc.numPages });
      } catch {
        if (!cancelled) setState({ phase: 'error' });
      }
    })();
    return () => {
      cancelled = true;
      void docRef.current?.destroy();
      docRef.current = undefined;
    };
  }, [base64]);

  return state;
}

// pdf.js setup (doc 05 §6.1). The worker is bundled by Vite as a same-origin asset (`?url`) —
// "plan A": it needs the environment CSP to allow `worker-src 'self'` (default Code App CSP is
// `worker-src 'none'`, see doc 05 §6.1 / doc 09). In local dev there is no such CSP, so it just
// works. This module is the ONLY place that touches pdfjs global config.

import * as pdfjsLib from 'pdfjs-dist';
import workerUrl from 'pdfjs-dist/build/pdf.worker.min.mjs?url';

pdfjsLib.GlobalWorkerOptions.workerSrc = workerUrl;

export { pdfjsLib };
export type PdfDoc = Awaited<ReturnType<typeof pdfjsLib.getDocument>['promise']>;

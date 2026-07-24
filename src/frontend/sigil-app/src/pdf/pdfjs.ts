// pdf.js setup. The worker is INLINED into the bundle (`?worker&inline`) and run from a
// blob — NOT fetched as a separate `.mjs` asset. Why: hosted in the Power Apps Code App, the storage
// proxy serves the worker `.mjs` with MIME `application/octet-stream`, and browsers reject module
// scripts with a non-JS MIME (strict checking) — so `?url` fails hosted even with the CSP fixed.
// Inlining sidesteps the CDN MIME entirely; it runs from a blob, so the environment CSP must allow
// `worker-src blob:`. In local dev there is no CSP, so it just works. This
// module is the ONLY place that touches pdfjs global config.

import * as pdfjsLib from 'pdfjs-dist';
import PdfWorker from 'pdfjs-dist/build/pdf.worker.min.mjs?worker&inline';

pdfjsLib.GlobalWorkerOptions.workerPort = new PdfWorker();

export { pdfjsLib };
export type PdfDoc = Awaited<ReturnType<typeof pdfjsLib.getDocument>['promise']>;

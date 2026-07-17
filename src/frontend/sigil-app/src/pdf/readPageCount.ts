// One-shot: decode a base64 PDF, read its page count, and destroy the doc. Used by the PDF step
// to stamp WizardPdf.pageCount (needed to validate zone pages) without mounting the full viewer.

import { pdfjsLib } from './pdfjs';
import { base64ToBytes } from '../api/binaries';

export async function readPdfPageCount(base64: string): Promise<number> {
  const bytes = await base64ToBytes(base64);
  const doc = await pdfjsLib.getDocument({ data: bytes }).promise;
  try {
    return doc.numPages;
  } finally {
    void doc.destroy();
  }
}

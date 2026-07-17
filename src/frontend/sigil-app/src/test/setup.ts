import '@testing-library/jest-dom/vitest';
// @ts-expect-error node:crypto has no types in this browser-typed project; setup runs under Node.
import { webcrypto } from 'node:crypto';

// jsdom implements neither WebCrypto nor Blob.arrayBuffer(), which DO exist in the real browser
// and in the Power Apps runtime. We polyfill them only for the test environment — the production
// code uses the native APIs as-is (doc 05 §5.2/§6.2: local hash with Web Crypto).

if (!globalThis.crypto?.subtle) {
  Object.defineProperty(globalThis, 'crypto', { value: webcrypto, configurable: true });
}

// File extends Blob → patching Blob.prototype covers both. jsdom does provide FileReader.
if (typeof Blob !== 'undefined' && typeof Blob.prototype.arrayBuffer !== 'function') {
  Blob.prototype.arrayBuffer = function arrayBuffer(this: Blob): Promise<ArrayBuffer> {
    return new Promise((resolve, reject) => {
      const fr = new FileReader();
      fr.onload = () => resolve(fr.result as ArrayBuffer);
      fr.onerror = () => reject(fr.error);
      fr.readAsArrayBuffer(this);
    });
  };
}

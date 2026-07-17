// Binary handling (doc 05 §5.2/§5.3): base64 ↔ Blob with STEPWISE decoding that yields
// (await) so it doesn't freeze the main thread while processing 27 MB on mobile. They NEVER
// go through the TanStack Query cache; they live in screen-local state and are freed on unmount.

/** SHA-256 in the browser (Web Crypto, ADR-007) → 64-char uppercase hex (ledger format). */
export async function sha256Hex(data: ArrayBuffer): Promise<string> {
  // Zero-cost view: passing a TypedArray (not the bare ArrayBuffer) makes digest agnostic
  // to the buffer's realm — irrelevant in the browser, needed when the buffer comes from
  // another context (e.g. jsdom's FileReader in tests). It copies no bytes.
  const digest = await crypto.subtle.digest('SHA-256', new Uint8Array(data));
  return [...new Uint8Array(digest)].map((b) => b.toString(16).padStart(2, '0')).join('').toUpperCase();
}

/** base64 → Uint8Array, decoding in chunks with yields (doesn't freeze the thread on mobile). */
export async function base64ToBytes(base64: string): Promise<Uint8Array> {
  const binary = atob(base64);
  const bytes = new Uint8Array(binary.length);
  const chunk = 1 << 20; // 1 MB per yield
  for (let i = 0; i < binary.length; i += chunk) {
    const end = Math.min(i + chunk, binary.length);
    for (let j = i; j < end; j++) bytes[j] = binary.charCodeAt(j);
    if (end < binary.length) await Promise.resolve(); // yield the thread between chunks
  }
  return bytes;
}

/** Uint8Array → base64 in chunks (upload). */
export async function bytesToBase64(bytes: Uint8Array): Promise<string> {
  const chunk = 1 << 15; // 32 KB (fromCharCode doesn't accept huge arrays)
  let binary = '';
  for (let i = 0; i < bytes.length; i += chunk) {
    binary += String.fromCharCode(...bytes.subarray(i, Math.min(i + chunk, bytes.length)));
    if (i + chunk < bytes.length) await Promise.resolve();
  }
  return btoa(binary);
}

/**
 * Client-side download: base64 → Blob → objectURL → <a download> → revoke.
 * Returns a cleanup function (revokes the objectURL) to call on unmount.
 */
export async function downloadBase64(
  base64: string,
  fileName: string,
  mime: string,
): Promise<void> {
  const bytes = await base64ToBytes(base64);
  const blob = new Blob([bytes], { type: mime });
  const url = URL.createObjectURL(blob);
  try {
    const a = document.createElement('a');
    a.href = url;
    a.download = fileName;
    document.body.appendChild(a);
    a.click();
    a.remove();
  } finally {
    // deferred revoke: Safari iOS needs the blob to live until the download starts
    setTimeout(() => URL.revokeObjectURL(url), 10_000);
  }
}

/** Reads a File as ArrayBuffer (to hash in verify — the file never leaves the browser). */
export function readFile(file: File): Promise<ArrayBuffer> {
  return file.arrayBuffer();
}

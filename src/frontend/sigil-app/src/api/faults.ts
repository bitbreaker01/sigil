// Extracts a clean, human-readable message from a Dataverse/SDK operation failure so the UI can
// show the real cause (e.g. a plugin's validation fault) instead of an opaque JSON blob. Kept as a
// pure module (no @microsoft/power-apps import) so it's unit-testable — powerApps.ts can't be
// loaded under vitest, but this can.
//
// The failure can arrive in several nested shapes, and — crucially — the SDK often puts the WHOLE
// OData fault as a JSON *string* inside `.message`, e.g.:
//   { message: '{"error":{"code":"0x...","message":"La imagen supera ..."}}' }
// so we unwrap objects AND parse JSON-encoded strings recursively to reach the innermost message.

/** Parses a string only if it looks like JSON; returns null otherwise (so plain text passes through). */
function tryParseJson(s: string): unknown | null {
  const t = s.trim();
  if (!t.startsWith('{') && !t.startsWith('[')) return null;
  try {
    return JSON.parse(t);
  } catch {
    return null;
  }
}

/** Recursively unwraps objects/JSON-strings to the innermost readable message; null if none found. */
function extract(error: unknown, depth = 0): string | null {
  if (error == null || depth > 6) return null;
  if (typeof error === 'string') {
    const parsed = tryParseJson(error);
    // A JSON-encoded fault → dig into it; plain text → return as-is.
    return parsed !== null ? (extract(parsed, depth + 1) ?? error) : error;
  }
  if (typeof error === 'object') {
    const e = error as { message?: unknown; error?: unknown };
    // Dataverse Web API fault nests the real message under `error`: { error: { code, message } }.
    if (e.error != null) {
      const inner = extract(e.error, depth + 1);
      if (inner) return inner;
    }
    if (e.message != null) return extract(e.message, depth + 1);
  }
  return null;
}

/**
 * Best-effort extraction of a readable message from a Dataverse/SDK failure. Returns the innermost
 * fault message when it can find one, otherwise a serialized fallback (still diagnosable in logs).
 */
export function dataverseFaultMessage(error: unknown): string {
  return extract(error) ?? `Dataverse operation failed: ${JSON.stringify(error ?? null)}`;
}

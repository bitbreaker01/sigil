// Extracts a human-readable message from a Dataverse/SDK operation failure so the UI can show the
// real cause (e.g. a plugin's validation fault) instead of an opaque generic error. Kept as a pure
// module (no @microsoft/power-apps import) so it's unit-testable — powerApps.ts can't be loaded
// under vitest, but this can.

/** The shapes a failed IOperationResult.error can take. */
interface DataverseFaultLike {
  message?: unknown;
  // Dataverse Web API faults nest the real message: { error: { code, message } }.
  error?: { message?: unknown } | null;
}

/**
 * Best-effort extraction of a readable message from a Dataverse/SDK failure.
 * Prefers the nested Web API fault message, then a flat message, else a serialized fallback.
 */
export function dataverseFaultMessage(error: unknown): string {
  if (error && typeof error === 'object') {
    const e = error as DataverseFaultLike;
    if (e.error && typeof e.error === 'object' && e.error.message != null) {
      return String(e.error.message);
    }
    if (e.message != null) return String(e.message);
  }
  // Unknown shape: surface something rather than nothing (still diagnosable in logs).
  return `Dataverse operation failed: ${JSON.stringify(error ?? null)}`;
}

// Onboarding logic (container hook — testable without render): loads the current signature, and
// runs the two-step replace flow (RF-02): upload → VALIDATE (preview, nothing persisted) → the user
// confirms → SAVE (creates the new active version, irreversible). The presentation (OnboardingScreen)
// is dumb and receives this state.

import { useCallback, useEffect, useRef, useState } from 'react';
import { sigilApi } from '../../api';
import type { MasterSignatureVersion } from '../../api/SigilApi';
import type { ValidateMasterSignatureOutput } from '../../api/contracts';

export type OnboardingState =
  | { phase: 'loading' }
  | { phase: 'ready'; currentSignature?: string; validatedOn?: string }
  | { phase: 'processing' }
  | { phase: 'editing'; source: string } // raw upload (data-URL) — framing/crop/rotate/flip before validating
  | { phase: 'preview'; normalized: string } // validated, NOT yet saved — awaiting confirmation
  | { phase: 'success'; normalized: string }
  | { phase: 'rejected'; reasons: string[] }
  | { phase: 'error'; message: string };

export interface UseOnboarding {
  state: OnboardingState;
  history: MasterSignatureVersion[];
  upload: (file: File) => void; // read the file → the editor (no backend yet)
  applyEdit: (base64: string) => void; // the edited PNG → VALIDATE (preview)
  save: () => void; // commit the previewed signature (call AFTER the user confirms the replacement)
  cancelPreview: () => void; // discard the preview, back to the current signature
  formatError: boolean;
}

/** FailureReasons: one reason per line (doc 04 §3.1); empty → generic. */
function reasonsFrom(r: ValidateMasterSignatureOutput): string[] {
  const reasons = (r.FailureReasons ?? '').split('\n').map((m) => m.trim()).filter(Boolean);
  return reasons.length ? reasons : ['common.genericError'];
}

/** A contract fault carries a readable backend message — prefer it over the opaque generic key. */
function messageFrom(e: unknown): string {
  console.error('[master-signature] failed:', e);
  return e instanceof Error && e.message.trim() ? e.message.trim() : 'common.genericError';
}

export function useOnboarding(): UseOnboarding {
  const [state, setState] = useState<OnboardingState>({ phase: 'loading' });
  const [history, setHistory] = useState<MasterSignatureVersion[]>([]);
  const [formatError, setFormatError] = useState(false);
  // Raw base64 of the previewed upload, kept out of state (it's what SAVE re-sends; the backend
  // re-validates and normalizes). Survives renders without re-triggering effects.
  const pendingBase64 = useRef<string | undefined>(undefined);

  const loadCurrent = useCallback(async () => {
    try {
      const fm = await sigilApi.getMasterSignature();
      const ready: OnboardingState = { phase: 'ready' };
      if (fm.ImageBase64) ready.currentSignature = fm.ImageBase64;
      if (fm.ValidatedOn) ready.validatedOn = fm.ValidatedOn;
      setState(ready);
    } catch {
      setState({ phase: 'error', message: 'common.genericError' });
    }
  }, []);

  // History is best-effort: if the backend API isn't available (real mode), just show none.
  const loadHistory = useCallback(async () => {
    try {
      setHistory(await sigilApi.getMasterSignatureHistory());
    } catch {
      setHistory([]);
    }
  }, []);

  useEffect(() => {
    void loadCurrent();
    void loadHistory();
  }, [loadCurrent, loadHistory]);

  const upload = useCallback((file: File) => {
    setFormatError(false);
    if (!file.type.includes('png') && !file.name.toLowerCase().endsWith('.png')) {
      setFormatError(true);
      return;
    }
    void (async () => {
      setState({ phase: 'processing' });
      try {
        const bytes = new Uint8Array(await file.arrayBuffer());
        const { bytesToBase64 } = await import('../../api/binaries');
        const base64 = await bytesToBase64(bytes);
        // Straight to the editor — the user frames/crops/rotates before we validate (nothing persisted).
        setState({ phase: 'editing', source: `data:image/png;base64,${base64}` });
      } catch (e) {
        setState({ phase: 'error', message: messageFrom(e) });
      }
    })();
  }, []);

  // The edited PNG (raw base64 from the editor) → VALIDATE (preview). Persists nothing until save().
  const applyEdit = useCallback((base64: string) => {
    void (async () => {
      setState({ phase: 'processing' });
      try {
        const r = await sigilApi.validateMasterSignature(base64); // PREVIEW — nothing persisted
        if (r.IsValid && r.NormalizedImageBase64) {
          pendingBase64.current = base64;
          setState({ phase: 'preview', normalized: r.NormalizedImageBase64 });
        } else {
          setState({ phase: 'rejected', reasons: reasonsFrom(r) });
        }
      } catch (e) {
        setState({ phase: 'error', message: messageFrom(e) });
      }
    })();
  }, []);

  const save = useCallback(() => {
    const base64 = pendingBase64.current;
    if (!base64) return;
    void (async () => {
      setState({ phase: 'processing' });
      try {
        const r = await sigilApi.saveMasterSignature(base64); // COMMIT — creates the new active version
        if (r.IsValid && r.NormalizedImageBase64) {
          pendingBase64.current = undefined;
          setState({ phase: 'success', normalized: r.NormalizedImageBase64 });
          void loadHistory(); // the save added a new version
        } else {
          setState({ phase: 'rejected', reasons: reasonsFrom(r) });
        }
      } catch (e) {
        setState({ phase: 'error', message: messageFrom(e) });
      }
    })();
  }, [loadHistory]);

  const cancelPreview = useCallback(() => {
    pendingBase64.current = undefined;
    void loadCurrent();
  }, [loadCurrent]);

  return { state, history, upload, applyEdit, save, cancelPreview, formatError };
}

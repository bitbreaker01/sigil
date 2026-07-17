// Onboarding logic (container hook — testable without render): loads the current signature,
// validates a new image, exposes failure reasons or the normalized preview. The presentation
// (OnboardingScreen) is dumb and receives this state.

import { useCallback, useEffect, useState } from 'react';
import { sigilApi } from '../../api';
import type { MasterSignatureVersion } from '../../api/SigilApi';

export type OnboardingState =
  | { phase: 'loading' }
  | { phase: 'ready'; currentSignature?: string; validatedOn?: string }
  | { phase: 'processing' }
  | { phase: 'success'; normalized: string }
  | { phase: 'rejected'; reasons: string[] }
  | { phase: 'error'; message: string };

export interface UseOnboarding {
  state: OnboardingState;
  history: MasterSignatureVersion[];
  upload: (file: File) => void;
  formatError: boolean;
}

export function useOnboarding(): UseOnboarding {
  const [state, setState] = useState<OnboardingState>({ phase: 'loading' });
  const [history, setHistory] = useState<MasterSignatureVersion[]>([]);
  const [formatError, setFormatError] = useState(false);

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
        const r = await sigilApi.validateMasterSignature(base64);
        if (r.IsValid && r.NormalizedImageBase64) {
          setState({ phase: 'success', normalized: r.NormalizedImageBase64 });
          void loadHistory(); // the upload added a new version
        } else {
          // FailureReasons: one reason per line (doc 04 §3.1)
          const reasons = (r.FailureReasons ?? '').split('\n').map((m) => m.trim()).filter(Boolean);
          setState({ phase: 'rejected', reasons: reasons.length ? reasons : ['common.genericError'] });
        }
      } catch {
        setState({ phase: 'error', message: 'common.genericError' });
      }
    })();
  }, [loadHistory]);

  return { state, history, upload, formatError };
}

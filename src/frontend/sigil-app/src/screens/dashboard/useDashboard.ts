// Dashboard container (doc 05 §4.1 + §5.1): the three lists + the first-run signal via TanStack
// Query. "My requests" polls every 5 s WHILE something is sealing, capped at 3 min (§5.1), after
// which auto-poll stops and the user can refresh manually. Binaries (the final PDF) are fetched
// DIRECTLY through the seam and streamed to a download — never through the Query cache (§5.2).

import { useCallback, useRef, useState } from 'react';
import { useQuery, useQueryClient, type Query } from '@tanstack/react-query';
import { sigilApi } from '../../api';
import type { TransactionView } from '../../api/SigilApi';
import { downloadBase64 } from '../../api/binaries';
import { POLLING_SEALING_MS, POLLING_CAP_MS } from '../../app/queryClient';
import { hasSealing, sealingErrors } from './dashboardModel';

const KEYS = {
  pending: ['dashboard', 'myPending'] as const,
  requests: ['dashboard', 'myRequests'] as const,
  participations: ['dashboard', 'myParticipations'] as const,
  masterSignature: ['dashboard', 'masterSignature'] as const,
};

export function useDashboard() {
  const qc = useQueryClient();
  const sealingSince = useRef<number | undefined>(undefined);
  const [sealingCapped, setSealingCapped] = useState(false); // 3-min poll cap reached (§5.1)
  const [actionError, setActionError] = useState(false); // retry/download failed

  const pending = useQuery({ queryKey: KEYS.pending, queryFn: () => sigilApi.myPending() });
  const participations = useQuery({ queryKey: KEYS.participations, queryFn: () => sigilApi.myParticipations() });
  const masterSignature = useQuery({ queryKey: KEYS.masterSignature, queryFn: () => sigilApi.getMasterSignature() });

  // Poll only while there's a sealing tx, and only until the 3-min cap (§5.1). At the cap we stop
  // auto-polling and surface a "still processing" message + manual refresh (sealingCapped).
  const requests = useQuery({
    queryKey: KEYS.requests,
    queryFn: () => sigilApi.myRequests(),
    refetchInterval: (query: Query<TransactionView[]>) => {
      const data = query.state.data;
      if (!data || !hasSealing(data)) {
        sealingSince.current = undefined;
        if (sealingCapped) setSealingCapped(false);
        return false;
      }
      const now = Date.now();
      if (sealingSince.current === undefined) sealingSince.current = now;
      if (now - sealingSince.current >= POLLING_CAP_MS) {
        if (!sealingCapped) setSealingCapped(true);
        return false;
      }
      return POLLING_SEALING_MS;
    },
  });

  const retrySealing = useCallback(async (txId: string) => {
    setActionError(false);
    try {
      await sigilApi.retrySealing(txId);
      sealingSince.current = undefined; // restart the poll window
      setSealingCapped(false);
      await qc.invalidateQueries({ queryKey: KEYS.requests });
    } catch {
      setActionError(true);
    }
  }, [qc]);

  // Final PDF: fetched directly (not cached) and streamed to a download (§5.2/§5.3).
  const downloadFinal = useCallback(async (tx: TransactionView) => {
    setActionError(false);
    try {
      const base64 = await sigilApi.getDocumentContent({ Target: tx.id, DocumentType: 'final' });
      await downloadBase64(base64, `${tx.name || 'document'}.pdf`, 'application/pdf');
    } catch {
      setActionError(true);
    }
  }, []);

  const refreshAll = useCallback(() => {
    setSealingCapped(false);
    sealingSince.current = undefined;
    void qc.invalidateQueries({ queryKey: KEYS.requests });
  }, [qc]);

  const dismissActionError = useCallback(() => setActionError(false), []);

  const requestList = requests.data ?? [];

  return {
    firstRun: masterSignature.isSuccess && !masterSignature.data?.ImageBase64,
    pending: pending.data ?? [],
    requests: requestList,
    participations: participations.data ?? [],
    sealingErrors: sealingErrors(requestList),
    isSealingActive: hasSealing(requestList),
    sealingCapped,
    actionError,
    loading: pending.isLoading || requests.isLoading || participations.isLoading,
    error: pending.isError || requests.isError || participations.isError,
    retrySealing,
    downloadFinal,
    refreshAll,
    dismissActionError,
  };
}

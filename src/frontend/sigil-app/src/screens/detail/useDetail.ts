// Detail container (doc 05 §4.4): the transaction + its participants + event timeline via TanStack
// Query. Polls while the tx is sealing (§5.1, 3-min cap → still-processing message). Creator-only
// actions (cancel RF-30, retry) are gated by identity — NON-authoritative UI hint; the backend
// enforces authorization (doc 04 §3.3). The final PDF is fetched directly, never cached (§5.2).

import { useCallback, useMemo, useRef, useState } from 'react';
import { useQuery, useQueryClient, type Query } from '@tanstack/react-query';
import { sigilApi } from '../../api';
import type { TransactionView } from '../../api/SigilApi';
import { downloadBase64 } from '../../api/binaries';
import { POLLING_SEALING_MS, POLLING_CAP_MS } from '../../app/queryClient';
import { isSealing } from '../dashboard/dashboardModel';

export function useDetail(txId: string) {
  const qc = useQueryClient();
  const sealingSince = useRef<number | undefined>(undefined);
  const [sealingCapped, setSealingCapped] = useState(false);
  const [actionError, setActionError] = useState(false);

  const base = useMemo(() => ['detail', txId] as const, [txId]);

  const tx = useQuery({
    queryKey: [...base, 'tx'],
    queryFn: () => sigilApi.getTransaction(txId),
    refetchInterval: (query: Query<TransactionView | undefined>) => {
      const data = query.state.data;
      if (!data || !isSealing(data.state)) {
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
  const participants = useQuery({ queryKey: [...base, 'participants'], queryFn: () => sigilApi.participantsOf(txId) });
  const events = useQuery({ queryKey: [...base, 'events'], queryFn: () => sigilApi.eventsOf(txId) });

  const invalidate = useCallback(() => qc.invalidateQueries({ queryKey: base }), [qc, base]);

  const cancel = useCallback(async (reason: string | undefined) => {
    setActionError(false);
    try {
      await sigilApi.cancelTransaction(reason && reason.trim() ? { Target: txId, Reason: reason.trim() } : { Target: txId });
      await invalidate();
    } catch {
      setActionError(true);
    }
  }, [txId, invalidate]);

  const retrySealing = useCallback(async () => {
    setActionError(false);
    try {
      await sigilApi.retrySealing(txId);
      sealingSince.current = undefined;
      setSealingCapped(false);
      await invalidate();
    } catch {
      setActionError(true);
    }
  }, [txId, invalidate]);

  const downloadFinal = useCallback(async () => {
    setActionError(false);
    try {
      const base64 = await sigilApi.getDocumentContent({ Target: txId, DocumentType: 'final' });
      await downloadBase64(base64, `${tx.data?.name || 'document'}.pdf`, 'application/pdf');
    } catch {
      setActionError(true);
    }
  }, [txId, tx.data?.name]);

  const refresh = useCallback(() => {
    sealingSince.current = undefined;
    setSealingCapped(false);
    void invalidate();
  }, [invalidate]);

  const me = sigilApi.currentUser().id;

  return {
    tx: tx.data,
    participants: participants.data ?? [],
    // Sort chronologically — the seam doesn't guarantee order, and both the timeline and the
    // reason extraction (scans from the end) depend on it (adversarial review W2).
    events: (events.data ?? []).slice().sort((a, b) => a.occurredOn.localeCompare(b.occurredOn)),
    isCreator: !!tx.data && !!me && tx.data.creatorId === me,
    loading: tx.isLoading || participants.isLoading || events.isLoading,
    notFound: tx.isSuccess && !tx.data,
    error: tx.isError || participants.isError || events.isError,
    sealingCapped,
    actionError,
    cancel,
    retrySealing,
    downloadFinal,
    refresh,
    dismissActionError: useCallback(() => setActionError(false), []),
  };
}

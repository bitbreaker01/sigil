// Dashboard container (doc 05 §4.1 + §5.1): the three lists + the first-run signal via TanStack
// Query. Requests & Participations are infinite-scroll (recent-first, server-side paged) so the
// dashboard never loads everything (§5.1). "My requests" polls every 5 s WHILE something is sealing,
// capped at 3 min, after which auto-poll stops and the user can refresh. Binaries (the final PDF)
// are fetched DIRECTLY through the seam and streamed to a download — never through the cache (§5.2).

import { useCallback, useMemo, useRef, useState } from 'react';
import { useInfiniteQuery, useQuery, useQueryClient } from '@tanstack/react-query';
import { sigilApi } from '../../api';
import type { TransactionView } from '../../api/SigilApi';
import { downloadBase64 } from '../../api/binaries';
import { POLLING_SEALING_MS, POLLING_CAP_MS } from '../../app/queryClient';
import { hasSealing, sealingErrors } from './dashboardModel';

const KEYS = {
  pending: ['dashboard', 'myPending'] as const,
  requests: ['dashboard', 'myRequestsPage'] as const,
  participations: ['dashboard', 'myParticipationsPage'] as const,
  masterSignature: ['dashboard', 'masterSignature'] as const,
};

const TX_COMPLETED = 159460004; // transaction state: Completed (server-side "completed only" filter)

export function useDashboard() {
  const qc = useQueryClient();
  const sealingSince = useRef<number | undefined>(undefined);
  const [sealingCapped, setSealingCapped] = useState(false); // 3-min poll cap reached (§5.1)
  const [actionError, setActionError] = useState(false); // retry/download failed
  const [onlyCompleted, setOnlyCompleted] = useState(false); // Participations "completed only" (server-side)

  const pending = useQuery({ queryKey: KEYS.pending, queryFn: () => sigilApi.myPending() });
  const masterSignature = useQuery({ queryKey: KEYS.masterSignature, queryFn: () => sigilApi.getMasterSignature() });

  const participations = useInfiniteQuery({
    queryKey: [...KEYS.participations, onlyCompleted],
    queryFn: ({ pageParam }) => sigilApi.myParticipationsPage(pageParam, onlyCompleted ? TX_COMPLETED : undefined),
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (last) => last.nextCookie || undefined,
  });

  // Poll only while a loaded page has a sealing tx, and only until the 3-min cap (§5.1). Sealing
  // txs are the most recent (just sent to seal) → they land on the first page, so watching the
  // loaded pages is enough. At the cap we stop auto-polling and surface a manual refresh.
  const requests = useInfiniteQuery({
    queryKey: KEYS.requests,
    queryFn: ({ pageParam }) => sigilApi.myRequestsPage(pageParam),
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (last) => last.nextCookie || undefined,
    refetchInterval: (query) => {
      const rows = (query.state.data?.pages ?? []).flatMap((p) => p.rows);
      if (!rows.length || !hasSealing(rows)) {
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

  const requestList = useMemo(() => requests.data?.pages.flatMap((p) => p.rows) ?? [], [requests.data]);
  const participationList = useMemo(() => participations.data?.pages.flatMap((p) => p.rows) ?? [], [participations.data]);

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

  return {
    firstRun: masterSignature.isSuccess && !masterSignature.data?.ImageBase64,
    pending: pending.data ?? [],
    requests: requestList,
    participations: participationList,
    sealingErrors: sealingErrors(requestList),
    isSealingActive: hasSealing(requestList),
    sealingCapped,
    actionError,
    onlyCompleted,
    setOnlyCompleted,
    loading: pending.isLoading || requests.isLoading || participations.isLoading,
    error: pending.isError || requests.isError || participations.isError,
    requestsHasMore: requests.hasNextPage,
    requestsLoadingMore: requests.isFetchingNextPage,
    loadMoreRequests: () => void requests.fetchNextPage(),
    participationsHasMore: participations.hasNextPage,
    participationsLoadingMore: participations.isFetchingNextPage,
    loadMoreParticipations: () => void participations.fetchNextPage(),
    retrySealing,
    downloadFinal,
    refreshAll,
    dismissActionError,
  };
}

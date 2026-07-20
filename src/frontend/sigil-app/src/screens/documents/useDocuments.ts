// Documents container hook (Phase 3): server-side paged search. Filters go to the SearchDocuments
// Custom API; results come back one page at a time and accumulate (infinite scroll) via the cookie.
// The free-text filter is debounced so we don't fire a query per keystroke.

import { useEffect, useMemo, useState } from 'react';
import { useInfiniteQuery } from '@tanstack/react-query';
import { sigilApi } from '../../api';
import { DEFAULT_FILTERS, toQuery, type DocumentFilters, type DocumentSort } from './documentsModel';

export function useDocuments(initialSignatureVersion?: number) {
  const [filters, setFilters] = useState<DocumentFilters>(
    initialSignatureVersion != null
      ? { ...DEFAULT_FILTERS, signatureVersion: initialSignatureVersion }
      : DEFAULT_FILTERS,
  );

  // Debounce the free-text filter (300ms) so typing doesn't trigger a search per keystroke.
  const [debouncedText, setDebouncedText] = useState(filters.text);
  useEffect(() => {
    const id = setTimeout(() => setDebouncedText(filters.text), 300);
    return () => clearTimeout(id);
  }, [filters.text]);

  const query = useMemo(
    () => toQuery({
      text: debouncedText,
      creatorId: filters.creatorId,
      participantId: filters.participantId,
      status: filters.status,
      signatureVersion: filters.signatureVersion,
      sort: filters.sort,
    }),
    [debouncedText, filters.creatorId, filters.participantId, filters.status, filters.signatureVersion, filters.sort],
  );

  const q = useInfiniteQuery({
    queryKey: ['documents', query],
    queryFn: ({ pageParam }) => sigilApi.searchDocuments(query, pageParam),
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (last) => last.nextCookie || undefined, // '' → no more pages
  });

  const rows = useMemo(() => q.data?.pages.flatMap((p) => p.rows) ?? [], [q.data]);
  const total = q.data?.pages[0]?.total ?? 0;

  return {
    rows,
    total,
    filters,
    loading: q.isLoading,
    error: q.isError,
    hasMore: q.hasNextPage,
    loadingMore: q.isFetchingNextPage,
    loadMore: () => void q.fetchNextPage(),
    hasVersionFilter: filters.signatureVersion !== 'all',
    setText: (text: string) => setFilters((f) => ({ ...f, text })),
    setCreator: (creatorId: string) => setFilters((f) => ({ ...f, creatorId })),
    setParticipant: (participantId: string) => setFilters((f) => ({ ...f, participantId })),
    setStatus: (status: number | 'all') => setFilters((f) => ({ ...f, status })),
    setSignatureVersion: (signatureVersion: number | 'all') => setFilters((f) => ({ ...f, signatureVersion })),
    setSort: (sort: DocumentSort) => setFilters((f) => ({ ...f, sort })),
    clearVersionFilter: () => setFilters((f) => ({ ...f, signatureVersion: 'all' })),
  };
}

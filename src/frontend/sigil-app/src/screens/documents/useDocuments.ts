// Documents container hook: loads the enriched doc list (myDocuments — created + participated,
// deduped and enriched) and exposes the client-side search/filter/sort state (documentsModel).
// Presentational DocumentsScreen consumes it.

import { useMemo, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { sigilApi } from '../../api';
import {
  creatorsOf, statusesOf, participantsOf, signatureVersionsOf, filterAndSort, DEFAULT_FILTERS,
  type DocumentFilters, type DocumentSort,
} from './documentsModel';

export function useDocuments(initialDocIds?: readonly string[]) {
  const docs = useQuery({ queryKey: ['documents', 'all'], queryFn: () => sigilApi.myDocuments() });

  const [filters, setFilters] = useState<DocumentFilters>(
    initialDocIds?.length ? { ...DEFAULT_FILTERS, docIds: initialDocIds } : DEFAULT_FILTERS,
  );

  const all = useMemo(() => docs.data ?? [], [docs.data]);
  const results = useMemo(() => filterAndSort(all, filters), [all, filters]);
  const creators = useMemo(() => creatorsOf(all), [all]);
  const statuses = useMemo(() => statusesOf(all), [all]);
  const participants = useMemo(() => participantsOf(all), [all]);
  const signatureVersions = useMemo(() => signatureVersionsOf(all), [all]);

  return {
    results,
    creators,
    statuses,
    participants,
    signatureVersions,
    filters,
    total: all.length,
    hasDocIdFilter: !!filters.docIds?.length,
    setText: (text: string) => setFilters((f) => ({ ...f, text })),
    setCreator: (creatorId: string) => setFilters((f) => ({ ...f, creatorId })),
    setParticipant: (participantId: string) => setFilters((f) => ({ ...f, participantId })),
    setStatus: (status: number | 'all') => setFilters((f) => ({ ...f, status })),
    setSignatureVersion: (signatureVersion: number | 'all') => setFilters((f) => ({ ...f, signatureVersion })),
    setSort: (sort: DocumentSort) => setFilters((f) => ({ ...f, sort })),
    // Drop the "documents from version N" pre-filter to browse everything.
    clearDocIds: () => setFilters(({ docIds: _drop, ...rest }) => rest),
    loading: docs.isLoading,
    error: docs.isError,
  };
}

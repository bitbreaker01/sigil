// Documents screen model (Phase 3): the UI filter state and its mapping to the server-side
// DocumentQuery. Filtering/sorting/paging now happen in the backend (SearchDocuments Custom API),
// so this file no longer filters in memory — it only holds the filter shape and builds the query.

import type { DocumentQuery } from '../../api/SigilApi';

export type DocumentSort =
  | 'nameAsc' | 'nameDesc'
  | 'createdDesc' | 'createdAsc'
  | 'sentDesc' | 'sentAsc'
  | 'completedDesc' | 'completedAsc';

/** A signer chosen in the "other participants" filter — id for the query, name for the chip. */
export interface SelectedUser {
  id: string;
  name: string;
}

export interface DocumentFilters {
  text: string; // matched against the document name (server-side, case-insensitive)
  creatorId: string; // '' = any creator
  participants: SelectedUser[]; // AND — the doc must include ALL of these signers
  status: number | 'all'; // transaction state choice value
  signatureVersion: number | 'all'; // version of MY master signature used to sign the doc
  sort: DocumentSort;
}

export const DEFAULT_FILTERS: DocumentFilters = {
  text: '', creatorId: '', participants: [], status: 'all', signatureVersion: 'all', sort: 'createdDesc',
};

export const PAGE_SIZE = 25;

/** Map the UI filter state to the backend query — omit the "any"/empty filters so they aren't sent. */
export function toQuery(f: DocumentFilters): DocumentQuery {
  const q: DocumentQuery = { sort: f.sort, pageSize: PAGE_SIZE };
  const text = f.text.trim();
  if (text) q.text = text;
  if (f.creatorId) q.creatorId = f.creatorId;
  if (f.participants.length) q.participantIds = f.participants.map((p) => p.id);
  if (f.status !== 'all') q.status = f.status;
  if (f.signatureVersion !== 'all') q.signatureVersion = f.signatureVersion;
  return q;
}

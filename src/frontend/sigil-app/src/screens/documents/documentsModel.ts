// Pure model for the Documents screen: client-side search / filter / sort over the enriched
// DocumentRow list (already merged + deduped by the API layer). Every filter here is a pure
// function of (rows, filters) — no I/O, fully unit-tested.

import type { DocumentRow } from '../../api/SigilApi';

export type DocumentSort =
  | 'nameAsc' | 'nameDesc'
  | 'createdDesc' | 'createdAsc'
  | 'sentDesc' | 'sentAsc'
  | 'completedDesc' | 'completedAsc';

export interface DocumentFilters {
  text: string; // matched against the document name (case-insensitive)
  creatorId: string; // '' = any creator
  participantId: string; // '' = any; matches docs where this user is ANY signer ("other participants")
  status: number | 'all'; // transaction state choice value
  signatureVersion: number | 'all'; // version of MY master signature used to sign the doc
  docIds?: readonly string[]; // pre-filter to a specific set (opened from the signature history)
  sort: DocumentSort;
}

export const DEFAULT_FILTERS: DocumentFilters = {
  text: '', creatorId: '', participantId: '', status: 'all', signatureVersion: 'all', sort: 'createdDesc',
};

/** Distinct creators present (drives the creator dropdown), by name. */
export function creatorsOf(docs: readonly DocumentRow[]): { id: string; name: string }[] {
  const byId = new Map<string, string>();
  for (const d of docs) if (d.creatorId && !byId.has(d.creatorId)) byId.set(d.creatorId, d.creatorName ?? d.creatorId);
  return [...byId.entries()]
    .map(([id, name]) => ({ id, name }))
    .sort((a, b) => a.name.localeCompare(b.name));
}

/** Distinct statuses present (drives the status dropdown). */
export function statusesOf(docs: readonly DocumentRow[]): number[] {
  return [...new Set(docs.map((d) => d.state))].sort((a, b) => a - b);
}

/** Distinct signers across ALL docs (drives the "other participants" dropdown), by name. */
export function participantsOf(docs: readonly DocumentRow[]): { id: string; name: string }[] {
  const byId = new Map<string, string>();
  for (const d of docs) for (const p of d.participants) {
    if (p.userId && !byId.has(p.userId)) byId.set(p.userId, p.name || p.userId);
  }
  return [...byId.entries()]
    .map(([id, name]) => ({ id, name }))
    .sort((a, b) => a.name.localeCompare(b.name));
}

/** Distinct versions of MY master signature present across docs (drives that dropdown), newest first. */
export function signatureVersionsOf(docs: readonly DocumentRow[]): number[] {
  const set = new Set<number>();
  for (const d of docs) if (d.mySignatureVersion != null) set.add(d.mySignatureVersion);
  return [...set].sort((a, b) => b - a);
}

/** Empty string sorts AFTER any date (a not-yet-completed doc goes last in a completed-date sort). */
function cmpStr(a: string | undefined, b: string | undefined, dir: 1 | -1): number {
  const av = a ?? '', bv = b ?? '';
  if (av === bv) return 0;
  if (!av) return 1; // missing always last, regardless of direction
  if (!bv) return -1;
  return av < bv ? -dir : dir;
}

export function filterAndSort(docs: readonly DocumentRow[], f: DocumentFilters): DocumentRow[] {
  const text = f.text.trim().toLowerCase();
  const ids = f.docIds ? new Set(f.docIds) : undefined;
  const filtered = docs.filter((d) =>
    (!ids || ids.has(d.id)) &&
    (!text || d.name.toLowerCase().includes(text)) &&
    (!f.creatorId || d.creatorId === f.creatorId) &&
    (!f.participantId || d.participants.some((p) => p.userId === f.participantId)) &&
    (f.status === 'all' || d.state === f.status) &&
    (f.signatureVersion === 'all' || d.mySignatureVersion === f.signatureVersion));

  const sorted = [...filtered];
  switch (f.sort) {
    case 'nameAsc': sorted.sort((a, b) => a.name.localeCompare(b.name)); break;
    case 'nameDesc': sorted.sort((a, b) => b.name.localeCompare(a.name)); break;
    case 'createdAsc': sorted.sort((a, b) => cmpStr(a.createdOn, b.createdOn, 1)); break;
    case 'createdDesc': sorted.sort((a, b) => cmpStr(a.createdOn, b.createdOn, -1)); break;
    case 'sentAsc': sorted.sort((a, b) => cmpStr(a.sentOn, b.sentOn, 1)); break;
    case 'sentDesc': sorted.sort((a, b) => cmpStr(a.sentOn, b.sentOn, -1)); break;
    case 'completedAsc': sorted.sort((a, b) => cmpStr(a.completedOn, b.completedOn, 1)); break;
    case 'completedDesc': sorted.sort((a, b) => cmpStr(a.completedOn, b.completedOn, -1)); break;
  }
  return sorted;
}

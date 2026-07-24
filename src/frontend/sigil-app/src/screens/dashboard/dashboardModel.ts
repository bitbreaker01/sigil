// Pure helpers for the dashboard: due-date urgency and state predicates.
// No React here so the logic is unit-testable; the hook composes these over the seam data.

import type { TransactionView } from '../../api/SigilApi';
import { transactionStateOf } from '../../domain/states';

export type DueLevel = 'overdue' | 'today' | 'soon' | 'none';

/** Visual urgency of an expiration date: overdue, <24h (today), <72h (soon), else none. */
export function dueLevel(expiresOn: string | undefined, nowMs: number): DueLevel {
  if (!expiresOn) return 'none';
  const t = new Date(expiresOn).getTime();
  if (Number.isNaN(t)) return 'none';
  const ms = t - nowMs;
  if (ms < 0) return 'overdue';
  if (ms < 24 * 3600_000) return 'today';
  if (ms < 72 * 3600_000) return 'soon';
  return 'none';
}

export function isSealing(state: number): boolean {
  return transactionStateOf(state) === 'sealing';
}
export function isSealingError(state: number): boolean {
  return transactionStateOf(state) === 'sealingError';
}
export function isCompleted(state: number): boolean {
  return transactionStateOf(state) === 'completed';
}

/** Any transaction currently sealing → the list should poll. */
export function hasSealing(txs: readonly TransactionView[]): boolean {
  return txs.some((t) => isSealing(t.state));
}

/** Sealing-error transactions surface at the top of "My requests" with a retry CTA. */
export function sealingErrors(txs: readonly TransactionView[]): TransactionView[] {
  return txs.filter((t) => isSealingError(t.state));
}

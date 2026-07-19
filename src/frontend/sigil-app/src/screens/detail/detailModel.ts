// Pure helpers for the detail screen (doc 05 §4.4): map event/participant choice values to their
// i18n keys (RNF-06), and extract the reject/cancel reason to show prominently.

import type { EventView } from '../../api/SigilApi';
import { EVENT_TYPE, PARTICIPANT_STATE, SIGNABLE_STATES, transactionStateOf } from '../../domain/states';

// ── Action gating by role/state (doc 05 §4.4, authoritative state machine doc 06 §1.1). ──

/** Creator cancel (T13): from Pending | Partially Signed | Sealing Error. NOT draft (that's
 *  DeleteDraft/T3), NOT terminals/sealing/completed. Sealing Error is included so a stuck seal
 *  isn't an eternal dashboard alert (doc 06 §54). */
export function canCancel(isCreator: boolean, state: number): boolean {
  const name = transactionStateOf(state);
  return isCreator && !!name && (SIGNABLE_STATES.has(name) || name === 'sealingError');
}

/** Creator retry (T10): only from Sealing Error. */
export function canRetry(isCreator: boolean, state: number): boolean {
  return isCreator && transactionStateOf(state) === 'sealingError';
}

/** Download the final PDF (RF-24): available to any viewer once the tx is Completed. */
export function canDownloadFinal(state: number): boolean {
  return transactionStateOf(state) === 'completed';
}

export function eventLabelKey(type: number): string | undefined {
  const name = EVENT_TYPE[type];
  return name ? `event.${name}` : undefined;
}

/** Verification events (RF-13) are user audit reads, not steps of the document's lifecycle — the
 *  timeline renders them as a muted, distinct lane so they don't blend with the process events. */
export function isVerificationEvent(type: number): boolean {
  return EVENT_TYPE[type] === 'verificationPerformed';
}

export function participantLabelKey(state: number): string | undefined {
  const name = PARTICIPANT_STATE[state];
  return name ? `participantState.${name}` : undefined;
}

/** The reason shown at the top when a transaction was rejected or cancelled (§4.4): the details of
 *  the most recent reject/cancel event (all participants share the transaction, so all can see it). */
export function terminationReason(events: readonly EventView[]): string | undefined {
  for (let i = events.length - 1; i >= 0; i--) {
    const name = EVENT_TYPE[events[i]!.type];
    if (name === 'rejected' || name === 'cancelledByCreator') {
      const details = events[i]!.details;
      return details && details.trim() ? details : undefined;
    }
  }
  return undefined;
}

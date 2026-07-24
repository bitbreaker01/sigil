// States by LOGICAL NAME: the backend returns numeric choice values
// (159460xxx); the UI maps them to a stable logical name, and EVERY visible label comes from
// i18n indexed by that name. Showing a formatted value directly is an i18n bug.
//
// The numeric values are those of the Choices Catalog (docs/referencia/catalogo-de-choices.md) — the ONLY place they live in the
// frontend, right here, verified against the backend. The rest of the code uses logical names.

export type TransactionState =
  | 'draft'
  | 'pendingSignature'
  | 'partiallySigned'
  | 'sealing'
  | 'completed'
  | 'rejected'
  | 'expired'
  | 'sealingError'
  | 'cancelled';

export type ParticipantState = 'pending' | 'activeTurn' | 'signed' | 'rejected';

export type TsaState = 'sealedWithTsa' | 'noTsaSeal' | 'resealPending';

export type EventType =
  | 'transactionCreated'
  | 'sentForSignature'
  | 'signatureRegistered'
  | 'rejected'
  | 'reminderScheduled'
  | 'sealingStarted'
  | 'sealingCompleted'
  | 'sealingError'
  | 'tsaResealObtained'
  | 'expired'
  | 'verificationPerformed'
  | 'cancelledByCreator'
  | 'tsaAbandoned';

export const TRANSACTION_STATE: Record<number, TransactionState> = {
  159460000: 'draft',
  159460001: 'pendingSignature',
  159460002: 'partiallySigned',
  159460003: 'sealing',
  159460004: 'completed',
  159460005: 'rejected',
  159460006: 'expired',
  159460007: 'sealingError',
  159460008: 'cancelled',
};

export const PARTICIPANT_STATE: Record<number, ParticipantState> = {
  159460000: 'pending',
  159460001: 'activeTurn',
  159460002: 'signed',
  159460003: 'rejected',
};

export const TSA_STATE: Record<number, TsaState> = {
  159460000: 'sealedWithTsa',
  159460001: 'noTsaSeal',
  159460002: 'resealPending',
};

export const EVENT_TYPE: Record<number, EventType> = {
  159460000: 'transactionCreated',
  159460001: 'sentForSignature',
  159460002: 'signatureRegistered',
  159460003: 'rejected',
  159460004: 'reminderScheduled',
  159460005: 'sealingStarted',
  159460006: 'sealingCompleted',
  159460007: 'sealingError',
  159460008: 'tsaResealObtained',
  159460009: 'expired',
  159460010: 'verificationPerformed',
  159460011: 'cancelledByCreator',
  159460012: 'tsaAbandoned',
};

// Routing travels as a contract token, not as a number — but the backend returns it numeric, so the
// number↔token map lives here too (this file is the single place the choice numbers live).
export type Routing = 'sequential' | 'parallel';

export const ROUTING_STATE: Record<number, Routing> = {
  159460000: 'sequential',
  159460001: 'parallel',
};

/** States in which the transaction still allows signing actions. */
export const SIGNABLE_STATES: ReadonlySet<TransactionState> = new Set([
  'pendingSignature',
  'partiallySigned',
]);

/** Terminal states: no actions, read-only. */
export const TERMINAL_STATES: ReadonlySet<TransactionState> = new Set([
  'completed',
  'rejected',
  'expired',
  'cancelled',
]);

export function transactionStateOf(value: number | undefined): TransactionState | undefined {
  return value === undefined ? undefined : TRANSACTION_STATE[value];
}

// Reverse lookup: the numeric value for a logical state. OData filters need the number; deriving it
// from the maps above keeps THIS file the single place the choice numbers live in the frontend.
function valueOf<T extends string>(map: Record<number, T>, name: T): number {
  const hit = Object.entries(map).find(([, v]) => v === name);
  if (!hit) throw new Error(`unknown state in states.ts: ${name}`);
  return Number(hit[0]);
}
export const txValue = (name: TransactionState): number => valueOf(TRANSACTION_STATE, name);
export const participantValue = (name: ParticipantState): number => valueOf(PARTICIPANT_STATE, name);
export const routingValue = (name: Routing): number => valueOf(ROUTING_STATE, name);

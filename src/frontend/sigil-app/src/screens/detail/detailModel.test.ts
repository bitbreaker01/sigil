import { describe, it, expect } from 'vitest';
import { eventLabelKey, participantLabelKey, terminationReason, canCancel, canRetry, canDownloadFinal, isVerificationEvent } from './detailModel';
import type { EventView } from '../../api/SigilApi';

// Choice values referenced only in this test.
const DRAFT = 159460000, PENDING = 159460001, PARTIAL = 159460002, SEALING = 159460003;
const COMPLETED = 159460004, REJECTED = 159460005, EXPIRED = 159460006, SEAL_ERROR = 159460007, CANCELLED = 159460008;

const ev = (type: number, details?: string): EventView => ({ id: `${type}`, type, occurredOn: '2026-07-16T10:00:00Z', ...(details ? { details } : {}) });

describe('label keys', () => {
  it('maps event and participant choice values to i18n keys', () => {
    expect(eventLabelKey(159460000)).toBe('event.transactionCreated');
    expect(eventLabelKey(159460011)).toBe('event.cancelledByCreator');
    expect(participantLabelKey(159460002)).toBe('participantState.signed');
  });
  it('returns undefined for unknown values', () => {
    expect(eventLabelKey(999)).toBeUndefined();
    expect(participantLabelKey(999)).toBeUndefined();
  });
  it('flags verification events as a distinct (non-lifecycle) lane', () => {
    expect(isVerificationEvent(159460010)).toBe(true); // verificationPerformed
    expect(isVerificationEvent(DRAFT)).toBe(false); // transactionCreated (lifecycle)
    expect(isVerificationEvent(159460006)).toBe(false); // sealingCompleted (lifecycle)
    expect(isVerificationEvent(999)).toBe(false); // unknown
  });
});

describe('terminationReason', () => {
  it('returns the details of the latest reject/cancel event', () => {
    const events = [ev(159460000, 'Request created'), ev(159460003, 'Rejected: too vague')];
    expect(terminationReason(events)).toBe('Rejected: too vague');
  });
  it('prefers the most recent termination event', () => {
    const events = [ev(159460003, 'first'), ev(159460011, 'Cancelled by creator')];
    expect(terminationReason(events)).toBe('Cancelled by creator');
  });
  it('is undefined when there is no reject/cancel event or the details are blank', () => {
    expect(terminationReason([ev(159460000, 'Request created')])).toBeUndefined();
    expect(terminationReason([ev(159460003, '   ')])).toBeUndefined();
    expect(terminationReason([])).toBeUndefined();
  });
});

describe('action gating', () => {
  it('creator can cancel from Pending, Partially Signed and Sealing Error — NOT draft/sealing/terminals', () => {
    for (const st of [PENDING, PARTIAL, SEAL_ERROR]) expect(canCancel(true, st)).toBe(true);
    for (const st of [DRAFT, SEALING, COMPLETED, REJECTED, EXPIRED, CANCELLED]) expect(canCancel(true, st)).toBe(false);
  });
  it('never lets a non-creator cancel', () => {
    for (const st of [PENDING, PARTIAL, SEAL_ERROR]) expect(canCancel(false, st)).toBe(false);
  });
  it('creator retry only from Sealing Error', () => {
    expect(canRetry(true, SEAL_ERROR)).toBe(true);
    expect(canRetry(true, SEALING)).toBe(false);
    expect(canRetry(false, SEAL_ERROR)).toBe(false);
  });
  it('download only when Completed (any viewer)', () => {
    expect(canDownloadFinal(COMPLETED)).toBe(true);
    expect(canDownloadFinal(PENDING)).toBe(false);
  });
});

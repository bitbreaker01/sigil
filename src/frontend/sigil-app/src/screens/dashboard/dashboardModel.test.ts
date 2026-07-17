import { describe, it, expect } from 'vitest';
import { dueLevel, isSealing, isSealingError, isCompleted, hasSealing, sealingErrors } from './dashboardModel';
import type { TransactionView } from '../../api/SigilApi';

const NOW = Date.parse('2026-07-16T12:00:00Z');
const inHours = (h: number) => new Date(NOW + h * 3600_000).toISOString();

describe('dueLevel', () => {
  it('overdue when the date is in the past', () => {
    expect(dueLevel(inHours(-1), NOW)).toBe('overdue');
  });
  it('today when under 24h', () => {
    expect(dueLevel(inHours(10), NOW)).toBe('today');
  });
  it('soon when under 72h', () => {
    expect(dueLevel(inHours(48), NOW)).toBe('soon');
  });
  it('none when far away, missing, or invalid', () => {
    expect(dueLevel(inHours(200), NOW)).toBe('none');
    expect(dueLevel(undefined, NOW)).toBe('none');
    expect(dueLevel('not-a-date', NOW)).toBe('none');
  });
});

describe('state predicates', () => {
  it('map the choice values to sealing / sealing-error / completed', () => {
    expect(isSealing(159460003)).toBe(true);
    expect(isSealingError(159460007)).toBe(true);
    expect(isCompleted(159460004)).toBe(true);
    expect(isSealing(159460004)).toBe(false);
  });
});

describe('list helpers', () => {
  const txs = [
    { id: '1', name: 'a', state: 159460003, routing: 'parallel', creatorId: 'x' },
    { id: '2', name: 'b', state: 159460007, routing: 'parallel', creatorId: 'x' },
    { id: '3', name: 'c', state: 159460004, routing: 'parallel', creatorId: 'x' },
  ] as TransactionView[];

  it('hasSealing detects an in-progress seal', () => {
    expect(hasSealing(txs)).toBe(true);
    expect(hasSealing(txs.filter((t) => t.state !== 159460003))).toBe(false);
  });
  it('sealingErrors picks only the errored ones', () => {
    expect(sealingErrors(txs).map((t) => t.id)).toEqual(['2']);
  });
});

import { describe, it, expect } from 'vitest';
import { toQuery, DEFAULT_FILTERS, PAGE_SIZE } from './documentsModel';

describe('toQuery', () => {
  it('sends only sort + pageSize when nothing is filtered', () => {
    expect(toQuery(DEFAULT_FILTERS)).toEqual({ sort: 'createdDesc', pageSize: PAGE_SIZE });
  });

  it('includes text (trimmed) only when non-empty', () => {
    expect(toQuery({ ...DEFAULT_FILTERS, text: '  acme ' }).text).toBe('acme');
    expect(toQuery({ ...DEFAULT_FILTERS, text: '   ' }).text).toBeUndefined();
  });

  it('omits "any" creator/participant/status/version', () => {
    const q = toQuery({ ...DEFAULT_FILTERS, creatorId: '', participants: [], status: 'all', signatureVersion: 'all' });
    expect(q.creatorId).toBeUndefined();
    expect(q.participantIds).toBeUndefined();
    expect(q.status).toBeUndefined();
    expect(q.signatureVersion).toBeUndefined();
  });

  it('sends the selected participants as an id list (AND)', () => {
    const q = toQuery({ ...DEFAULT_FILTERS, participants: [{ id: 'u2', name: 'Ana' }, { id: 'u3', name: 'Beto' }] });
    expect(q.participantIds).toEqual(['u2', 'u3']);
  });

  it('passes concrete filters through', () => {
    const q = toQuery({
      text: 'nda', creatorId: 'u1', participants: [{ id: 'u2', name: 'Ana' }],
      status: 159460004, signatureVersion: 2, sort: 'nameAsc',
    });
    expect(q).toEqual({
      sort: 'nameAsc', pageSize: PAGE_SIZE, text: 'nda',
      creatorId: 'u1', participantIds: ['u2'], status: 159460004, signatureVersion: 2,
    });
  });
});

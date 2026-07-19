import { describe, it, expect } from 'vitest';
import type { DocumentRow } from '../../api/SigilApi';
import {
  creatorsOf, statusesOf, participantsOf, signatureVersionsOf, filterAndSort, DEFAULT_FILTERS,
} from './documentsModel';

const COMPLETED = 159460004, PENDING = 159460001;

const doc = (over: Partial<DocumentRow>): DocumentRow => ({
  id: 'x', name: 'Doc', state: PENDING, routing: 'sequential', creatorId: 'u1', creatorName: 'Ana',
  participants: [], ...over,
});

describe('creatorsOf / statusesOf', () => {
  it('lists distinct creators by name and distinct statuses', () => {
    const docs = [doc({ creatorId: 'u1', creatorName: 'Ana', state: PENDING }),
      doc({ creatorId: 'u2', creatorName: 'Beto', state: COMPLETED }),
      doc({ creatorId: 'u1', creatorName: 'Ana', state: COMPLETED })];
    expect(creatorsOf(docs)).toEqual([{ id: 'u1', name: 'Ana' }, { id: 'u2', name: 'Beto' }]);
    expect(statusesOf(docs)).toEqual([PENDING, COMPLETED].sort((a, b) => a - b));
  });
});

describe('participantsOf / signatureVersionsOf', () => {
  it('lists distinct signers across all docs, by name', () => {
    const docs = [
      doc({ participants: [{ userId: 'u2', name: 'Beto' }, { userId: 'u3', name: 'Ana' }] }),
      doc({ participants: [{ userId: 'u2', name: 'Beto' }] }),
    ];
    expect(participantsOf(docs)).toEqual([{ id: 'u3', name: 'Ana' }, { id: 'u2', name: 'Beto' }]);
  });
  it('lists distinct signature versions present, newest first', () => {
    const docs = [doc({ mySignatureVersion: 1 }), doc({ mySignatureVersion: 3 }), doc({ mySignatureVersion: 1 }), doc({})];
    expect(signatureVersionsOf(docs)).toEqual([3, 1]);
  });
});

describe('filterAndSort', () => {
  const docs = [
    doc({ id: 'a', name: 'Contrato ACME', creatorId: 'u1', state: COMPLETED, createdOn: '2026-01-01', sentOn: '2026-01-02', completedOn: '2026-01-05', mySignatureVersion: 1, participants: [{ userId: 'u2', name: 'Beto' }] }),
    doc({ id: 'b', name: 'NDA Falcon', creatorId: 'u2', state: PENDING, createdOn: '2026-01-04', sentOn: '2026-01-03', mySignatureVersion: 2, participants: [{ userId: 'u3', name: 'Caro' }] }),
    doc({ id: 'c', name: 'Acuerdo beta', creatorId: 'u1', state: COMPLETED, createdOn: '2026-01-02', sentOn: '2026-01-01', completedOn: '2026-01-09', mySignatureVersion: 1, participants: [{ userId: 'u2', name: 'Beto' }, { userId: 'u3', name: 'Caro' }] }),
  ];

  it('filters by text (name, case-insensitive)', () => {
    expect(filterAndSort(docs, { ...DEFAULT_FILTERS, text: 'acme' }).map((d) => d.id)).toEqual(['a']);
  });
  it('filters by creator and by status', () => {
    expect(filterAndSort(docs, { ...DEFAULT_FILTERS, creatorId: 'u1' }).map((d) => d.id).sort()).toEqual(['a', 'c']);
    expect(filterAndSort(docs, { ...DEFAULT_FILTERS, status: PENDING }).map((d) => d.id)).toEqual(['b']);
  });
  it('filters by other participant (any signer on the doc)', () => {
    expect(filterAndSort(docs, { ...DEFAULT_FILTERS, participantId: 'u3' }).map((d) => d.id).sort()).toEqual(['b', 'c']);
  });
  it('filters by my signature version', () => {
    expect(filterAndSort(docs, { ...DEFAULT_FILTERS, signatureVersion: 1 }).map((d) => d.id).sort()).toEqual(['a', 'c']);
    expect(filterAndSort(docs, { ...DEFAULT_FILTERS, signatureVersion: 2 }).map((d) => d.id)).toEqual(['b']);
  });
  it('pre-filters to a set of ids (from the signature history link)', () => {
    expect(filterAndSort(docs, { ...DEFAULT_FILTERS, docIds: ['a', 'c'] }).map((d) => d.id).sort()).toEqual(['a', 'c']);
  });
  it('sorts by created date desc by default', () => {
    // createdDesc: b(01-04) > c(01-02) > a(01-01)
    expect(filterAndSort(docs, DEFAULT_FILTERS).map((d) => d.id)).toEqual(['b', 'c', 'a']);
  });
  it('sorts by sent date desc', () => {
    expect(filterAndSort(docs, { ...DEFAULT_FILTERS, sort: 'sentDesc' }).map((d) => d.id)).toEqual(['b', 'a', 'c']);
  });
  it('sorts by completed date, missing dates last', () => {
    // completedDesc: c(01-09) > a(01-05) > b(none, last)
    expect(filterAndSort(docs, { ...DEFAULT_FILTERS, sort: 'completedDesc' }).map((d) => d.id)).toEqual(['c', 'a', 'b']);
  });
  it('sorts by name', () => {
    expect(filterAndSort(docs, { ...DEFAULT_FILTERS, sort: 'nameAsc' }).map((d) => d.name)).toEqual(['Acuerdo beta', 'Contrato ACME', 'NDA Falcon']);
  });
});

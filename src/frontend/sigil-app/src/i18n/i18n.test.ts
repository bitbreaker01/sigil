// M11 (doc 11 §4) — completeness of i18n resources and consistency of states by logical name.
// The completeness test is defense in depth: `en satisfies Resources` already guarantees it
// at compile time, but a nested object with missing keys could slip through — here we close that.

import { describe, it, expect } from 'vitest';
import { es } from './es';
import { en } from './en';
import {
  TRANSACTION_STATE,
  PARTICIPANT_STATE,
  TSA_STATE,
  EVENT_TYPE,
} from '../domain/states';
import {
  validatePdf,
  validateParticipants,
  validateZones,
  validateHeader,
} from '../api/validations';
import type { ParticipantInput } from '../api/contracts';

function deepKeys(obj: Record<string, unknown>, prefix = ''): string[] {
  return Object.entries(obj).flatMap(([k, v]) => {
    const key = prefix ? `${prefix}.${k}` : k;
    return typeof v === 'object' && v !== null
      ? deepKeys(v as Record<string, unknown>, key)
      : [key];
  });
}

describe('i18n — completeness (M11)', () => {
  it('es and en have EXACTLY the same keys', () => {
    const keysEs = deepKeys(es).sort();
    const keysEn = deepKeys(en).sort();
    expect(keysEn).toEqual(keysEs);
  });

  it('no label is empty in either language', () => {
    for (const resource of [es, en]) {
      const empty = deepKeys(resource).filter((key) => {
        const value = key.split('.').reduce<unknown>((o, k) => (o as Record<string, unknown>)?.[k], resource);
        return typeof value === 'string' && value.trim() === '';
      });
      expect(empty).toEqual([]);
    }
  });
});

describe('states by logical name — every choice value has its label in BOTH languages', () => {
  const cases: [string, Record<number, string>, Record<string, string>, Record<string, string>][] = [
    ['transaction', TRANSACTION_STATE, es.transactionState, en.transactionState],
    ['participant', PARTICIPANT_STATE, es.participantState, en.participantState],
    ['tsa', TSA_STATE, es.tsa, en.tsa],
    ['event', EVENT_TYPE, es.event, en.event],
  ];

  it.each(cases)('%s: all logical names have es and en labels', (_name, map, labelsEs, labelsEn) => {
    for (const logicalName of Object.values(map)) {
      expect(labelsEs[logicalName], `missing es label for '${logicalName}'`).toBeTruthy();
      expect(labelsEn[logicalName], `missing en label for '${logicalName}'`).toBeTruthy();
    }
  });
});

describe('validation keys — every key the validators emit has a label in BOTH languages', () => {
  // Drive the validators with inputs that trip every branch, then harvest the emitted keys.
  // If a new validation key is added without its i18n label, this fails (closes the M11 blind
  // spot flagged in review: the state/event checks above did NOT cover validation.* keys).
  function harvestValidationKeys(): Set<string> {
    const keys = new Set<string>();
    const collect = (errors: string[]) => errors.forEach((e) => keys.add(e));

    const txtFile = new File([new Uint8Array(4)], 'x.txt', { type: 'text/plain' });
    collect(validatePdf(txtFile, 100).errors); // pdfExtension
    const pdfFile = new File([new Uint8Array(2048)], 'x.pdf', { type: 'application/pdf' });
    collect(validatePdf(pdfFile, 0).errors); // pdfSize

    collect(validateParticipants([], 'parallel', 20).errors); // noParticipants
    const many: ParticipantInput[] = Array.from({ length: 3 }, (_v, i) => ({ userId: `u${i}` }));
    collect(validateParticipants(many, 'parallel', 2).errors); // maxParticipants
    collect(validateParticipants([{ userId: 'a' }, { userId: 'a' }], 'parallel', 20).errors); // duplicate
    collect(validateParticipants([{ userId: '' }], 'parallel', 20).errors); // participantWithoutId
    collect(validateParticipants([{ userId: 'a' }], 'sequential', 20).errors); // sequentialWithoutOrder
    collect(validateParticipants([{ userId: 'a', order: 1 }, { userId: 'b', order: 3 }], 'sequential', 20).errors); // orderWithGaps
    collect(validateParticipants([{ userId: 'a', order: 1 }], 'parallel', 20).errors); // parallelWithOrder

    const good: ReadonlySet<string> = new Set(['a']);
    collect(validateZones([{ userId: 'z', page: 1, x: 0, y: 0, w: 10, h: 10 }], good, 3).errors); // orphanZone
    collect(validateZones([{ userId: 'a', page: 9, x: 0, y: 0, w: 10, h: 10 }], good, 3).errors); // zonePage
    collect(validateZones([{ userId: 'a', page: 1, x: -1, y: 0, w: 10, h: 10 }], good, 3).errors); // zoneOutOfRange
    collect(validateZones([{ userId: 'a', page: 1, x: 0, y: 0, w: 0, h: 10 }], good, 3).errors); // zoneSize
    collect(validateZones([{ userId: 'a', page: 1, x: 95, y: 0, w: 10, h: 10 }], good, 3).errors); // zoneOverflow

    collect(validateHeader('', undefined, undefined).errors); // titleRequired
    collect(validateHeader('x'.repeat(201), undefined, undefined).errors); // titleTooLong
    collect(validateHeader('ok', 'm'.repeat(2001), undefined).errors); // messageTooLong
    collect(validateHeader('ok', undefined, -1).errors); // invalidExpiration
    return keys;
  }

  it('harvests all validation.* keys and each exists in es and en', () => {
    const keys = harvestValidationKeys();
    expect(keys.size).toBeGreaterThanOrEqual(18); // guard against the harvest silently under-covering
    for (const fullKey of keys) {
      expect(fullKey.startsWith('validation.'), `unexpected key shape: ${fullKey}`).toBe(true);
      const leaf = fullKey.slice('validation.'.length);
      expect((es.validation as Record<string, string>)[leaf], `missing es label for '${fullKey}'`).toBeTruthy();
      expect((en.validation as Record<string, string>)[leaf], `missing en label for '${fullKey}'`).toBeTruthy();
    }
  });
});

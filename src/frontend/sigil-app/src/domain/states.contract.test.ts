// Contract test — the frontend twin of the backend's ChoicesTests.
//
// The numeric choice values live in exactly ONE place in the frontend: the maps in `states.ts`
// (powerApps.ts and everything else derive from them). Those numbers mirror Appendix A in
// docs/referencia/catalogo-de-choices.md — the same source the backend enums are tested
// against. This test imports that Appendix (Vite `?raw`) and asserts the value SETS match exactly,
// so a change to the choices in Dataverse can't silently drift the UI (e.g. a "Sealing" tx rendering
// as "Pending") without turning a test red.

import { describe, it, expect } from 'vitest';
import appendixMd from '../../../../../docs/referencia/catalogo-de-choices.md?raw';
import { TRANSACTION_STATE, PARTICIPANT_STATE, TSA_STATE, EVENT_TYPE, ROUTING_STATE } from './states';

// Parse Appendix A → choice logical name → set of numeric values. Same table shape the backend parses.
function appendixA(): Map<string, Set<number>> {
  const row = /^\|\s*(sanic_sigil_choice_\w+)\s*\|\s*.+?\s*\|\s*(\d+)\s*\|/;
  const map = new Map<string, Set<number>>();
  for (const line of appendixMd.split(/\r?\n/)) {
    const m = row.exec(line);
    if (!m) continue;
    const choice = m[1];
    const value = m[2];
    if (choice === undefined || value === undefined) continue;
    let set = map.get(choice);
    if (!set) {
      set = new Set();
      map.set(choice, set);
    }
    set.add(Number(value));
  }
  return map;
}

const appendix = appendixA();
const asc = (s: Iterable<number>): number[] => [...s].sort((a, b) => a - b);

describe('states.ts mirrors the Choices Catalog — the frontend twin of ChoicesTests', () => {
  it('parsed the Appendix (the 5 global choices are present)', () => {
    expect([...appendix.keys()].sort()).toEqual([
      'sanic_sigil_choice_eventtype',
      'sanic_sigil_choice_participantstatus',
      'sanic_sigil_choice_routingtype',
      'sanic_sigil_choice_transactionstatus',
      'sanic_sigil_choice_tsastatus',
    ]);
  });

  it.each([
    ['sanic_sigil_choice_transactionstatus', TRANSACTION_STATE],
    ['sanic_sigil_choice_participantstatus', PARTICIPANT_STATE],
    ['sanic_sigil_choice_tsastatus', TSA_STATE],
    ['sanic_sigil_choice_eventtype', EVENT_TYPE],
    ['sanic_sigil_choice_routingtype', ROUTING_STATE],
  ] as const)('%s — numeric values match the Appendix exactly', (choice, stateMap) => {
    const fromDoc = appendix.get(choice);
    expect(fromDoc, `${choice} missing from Appendix A`).toBeDefined();
    if (!fromDoc) return;
    const fromCode = Object.keys(stateMap).map(Number);
    expect(asc(fromCode)).toEqual(asc(fromDoc));
  });

  it('every mirrored value is within the publisher prefix range 15946xxxx', () => {
    const all = [TRANSACTION_STATE, PARTICIPANT_STATE, TSA_STATE, EVENT_TYPE, ROUTING_STATE].flatMap((m) =>
      Object.keys(m).map(Number),
    );
    for (const v of all) {
      expect(v).toBeGreaterThanOrEqual(159460000);
      expect(v).toBeLessThanOrEqual(159469999);
    }
  });
});

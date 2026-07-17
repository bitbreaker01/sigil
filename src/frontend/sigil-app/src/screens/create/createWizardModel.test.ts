// Tests of the PURE wizard model: step gating, RF-28 zone completeness, and the CreateTransaction
// input construction. No React, no pdf.js — just the logic that decides what's valid and what ships.

import { describe, it, expect } from 'vitest';
import {
  emptyDraft,
  pdfStepErrors,
  participantsStepErrors,
  zonesStepErrors,
  participantsMissingZone,
  canAdvance,
  canSend,
  canSaveDraft,
  nextStep,
  prevStep,
  buildCreateInput,
  DEFAULT_LIMITS,
  type WizardDraft,
  type WizardPdf,
} from './createWizardModel';

function pdf(pageCount = 2): WizardPdf {
  return { file: new File([new Uint8Array(1024)], 'doc.pdf', { type: 'application/pdf' }), base64: 'JVBERi0=', pageCount };
}

function fullDraft(): WizardDraft {
  return {
    pdf: pdf(2),
    name: 'Contract',
    message: '',
    routing: 'parallel',
    participants: [
      { userId: 'a', name: 'Ana' },
      { userId: 'b', name: 'Bruno' },
    ],
    zones: [
      { id: 'z1', userId: 'a', page: 1, x: 10, y: 10, w: 20, h: 8 },
      { id: 'z2', userId: 'b', page: 2, x: 10, y: 50, w: 20, h: 8 },
    ],
  };
}

describe('pdf step', () => {
  it('requires a PDF', () => {
    expect(pdfStepErrors(emptyDraft(), DEFAULT_LIMITS)).toContain('validation.pdfExtension');
  });
  it('requires a title', () => {
    const d = { ...emptyDraft(), pdf: pdf() };
    expect(pdfStepErrors(d, DEFAULT_LIMITS)).toContain('validation.titleRequired');
  });
  it('flags a PDF over the size limit before anything else', () => {
    const d = { ...emptyDraft(), pdf: pdf(), name: 'X' };
    expect(pdfStepErrors(d, { maxPdfKb: 0, maxParticipants: 20 })).toContain('validation.pdfSize');
  });
  it('passes with a valid PDF + title', () => {
    const d = { ...emptyDraft(), pdf: pdf(), name: 'Contract' };
    expect(pdfStepErrors(d, DEFAULT_LIMITS)).toEqual([]);
  });
});

describe('participants step', () => {
  it('requires at least one participant', () => {
    const d = { ...fullDraft(), participants: [] };
    expect(participantsStepErrors(d, DEFAULT_LIMITS)).toContain('validation.noParticipants');
  });
  it('rejects sequential without order', () => {
    const d: WizardDraft = { ...fullDraft(), routing: 'sequential', participants: [{ userId: 'a', name: 'Ana' }] };
    expect(participantsStepErrors(d, DEFAULT_LIMITS)).toContain('validation.sequentialWithoutOrder');
  });
  it('accepts sequential 1..N without gaps', () => {
    const d: WizardDraft = {
      ...fullDraft(),
      routing: 'sequential',
      participants: [
        { userId: 'a', name: 'Ana', order: 1 },
        { userId: 'b', name: 'Bruno', order: 2 },
      ],
    };
    expect(participantsStepErrors(d, DEFAULT_LIMITS)).toEqual([]);
  });
});

describe('zones step (RF-28)', () => {
  it('is complete when every participant has a zone', () => {
    expect(zonesStepErrors(fullDraft())).toEqual([]);
    expect(participantsMissingZone(fullDraft())).toEqual([]);
  });
  it('blocks when a participant has no zone', () => {
    const d = { ...fullDraft(), zones: [{ id: 'z1', userId: 'a', page: 1, x: 10, y: 10, w: 20, h: 8 }] };
    expect(zonesStepErrors(d)).toContain('validation.participantsWithoutZone');
    expect(participantsMissingZone(d).map((p) => p.userId)).toEqual(['b']);
  });
  it('flags a zone on a non-existent page', () => {
    const d = { ...fullDraft(), zones: [{ id: 'z1', userId: 'a', page: 9, x: 10, y: 10, w: 20, h: 8 }, { id: 'z2', userId: 'b', page: 1, x: 0, y: 0, w: 5, h: 5 }] };
    expect(zonesStepErrors(d)).toContain('validation.zonePage');
  });
  it('flags a zone that overflows the page', () => {
    const d = { ...fullDraft(), zones: [{ id: 'z1', userId: 'a', page: 1, x: 95, y: 10, w: 20, h: 8 }, { id: 'z2', userId: 'b', page: 2, x: 0, y: 0, w: 5, h: 5 }] };
    expect(zonesStepErrors(d)).toContain('validation.zoneOverflow');
  });
});

describe('gating: advance / send / save draft', () => {
  it('a full valid draft can advance every step and can send', () => {
    const d = fullDraft();
    expect(canAdvance('pdf', d, DEFAULT_LIMITS)).toBe(true);
    expect(canAdvance('participants', d, DEFAULT_LIMITS)).toBe(true);
    expect(canAdvance('zones', d, DEFAULT_LIMITS)).toBe(true);
    expect(canSend(d, DEFAULT_LIMITS)).toBe(true);
  });
  it('cannot send when zones are incomplete, even if the rest is valid', () => {
    const d = { ...fullDraft(), zones: [] };
    expect(canSend(d, DEFAULT_LIMITS)).toBe(false);
    expect(canAdvance('participants', d, DEFAULT_LIMITS)).toBe(true); // rest is fine
  });
  it('save draft needs only a PDF + name (incomplete allowed)', () => {
    expect(canSaveDraft(emptyDraft())).toBe(false);
    expect(canSaveDraft({ ...emptyDraft(), pdf: pdf(), name: 'Draft' })).toBe(true);
  });
});

describe('step navigation', () => {
  it('clamps at the ends', () => {
    expect(nextStep('pdf')).toBe('participants');
    expect(nextStep('review')).toBe('review');
    expect(prevStep('pdf')).toBe('pdf');
    expect(prevStep('zones')).toBe('participants');
  });
});

describe('buildCreateInput', () => {
  it('maps a parallel draft to the CreateTransaction contract', () => {
    const input = buildCreateInput(fullDraft());
    expect(input.Name).toBe('Contract');
    expect(input.RoutingType).toBe('parallel');
    expect(input.PdfBase64).toBe('JVBERi0=');
    expect(JSON.parse(input.ParticipantsJson)).toEqual([{ userId: 'a' }, { userId: 'b' }]);
    expect(input.ZonesJson).toBeDefined();
    expect(JSON.parse(input.ZonesJson!)).toHaveLength(2);
    expect(input.Message).toBeUndefined(); // empty message omitted
  });
  it('emits order only for sequential and includes a trimmed message', () => {
    const d: WizardDraft = {
      ...fullDraft(),
      routing: 'sequential',
      message: '  please sign  ',
      participants: [
        { userId: 'a', name: 'Ana', order: 1 },
        { userId: 'b', name: 'Bruno', order: 2 },
      ],
    };
    const input = buildCreateInput(d);
    expect(JSON.parse(input.ParticipantsJson)).toEqual([{ userId: 'a', order: 1 }, { userId: 'b', order: 2 }]);
    expect(input.Message).toBe('please sign');
  });
  it('throws if called without a PDF (guard)', () => {
    expect(() => buildCreateInput(emptyDraft())).toThrow();
  });
});

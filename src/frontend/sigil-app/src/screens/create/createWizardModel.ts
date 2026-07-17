// Pure model of the create wizard (doc 05 §4.2). NO React here — all step gating, validation,
// and the CreateTransaction input construction live as pure functions so they are trivially
// testable. The hook (useCreateWizard) holds React state and calls these + the seam.
//
// Steps (doc 05 §4.2): (1) PDF + request header, (2) signers + routing, (3) MANDATORY signature
// zones (§6.3, RF-28: every signer ≥1 zone; the step can't be skipped and "Send" stays blocked
// until complete), (4) review → send or save draft.

import type { Routing } from '../../domain/states';
import type { CreateTransactionInput } from '../../api/contracts';
import {
  validatePdf,
  validateParticipants,
  validateZones,
  validateHeader,
  participantsWithoutZone,
} from '../../api/validations';

export type WizardStep = 'pdf' | 'participants' | 'zones' | 'review';
export const WIZARD_STEPS: readonly WizardStep[] = ['pdf', 'participants', 'zones', 'review'];

export interface WizardPdf {
  file: File;
  base64: string;
  pageCount: number;
}

export interface WizardParticipant {
  userId: string;
  name: string;
  email?: string;
  order?: number; // sequential only
}

export interface WizardZone {
  id: string; // client-local id (drag handle key), NOT sent to the backend
  userId: string;
  page: number;
  x: number;
  y: number;
  w: number;
  h: number;
}

export interface WizardDraft {
  pdf?: WizardPdf;
  name: string;
  message: string;
  routing: Routing;
  expirationDays?: number;
  participants: WizardParticipant[];
  zones: WizardZone[];
}

export interface WizardLimits {
  maxPdfKb: number;
  maxParticipants: number;
}

export const DEFAULT_LIMITS: WizardLimits = { maxPdfKb: 27_000, maxParticipants: 20 };

export function emptyDraft(): WizardDraft {
  return { name: '', message: '', routing: 'sequential', participants: [], zones: [] };
}

// ── Per-step validation → arrays of i18n keys ──

export function pdfStepErrors(draft: WizardDraft, limits: WizardLimits): string[] {
  const errors: string[] = [];
  if (!draft.pdf) errors.push('validation.pdfExtension'); // no PDF chosen yet
  else errors.push(...validatePdf(draft.pdf.file, limits.maxPdfKb).errors);
  errors.push(...validateHeader(draft.name, draft.message || undefined, draft.expirationDays).errors);
  return dedupe(errors);
}

export function participantsStepErrors(draft: WizardDraft, limits: WizardLimits): string[] {
  return validateParticipants(
    draft.participants.map((p) => (p.order === undefined ? { userId: p.userId } : { userId: p.userId, order: p.order })),
    draft.routing,
    limits.maxParticipants,
  ).errors;
}

export function zonesStepErrors(draft: WizardDraft): string[] {
  const pageCount = draft.pdf?.pageCount ?? 0;
  const participantIds = new Set(draft.participants.map((p) => p.userId));
  const geometry = validateZones(
    draft.zones.map((z) => ({ userId: z.userId, page: z.page, x: z.x, y: z.y, w: z.w, h: z.h })),
    participantIds,
    pageCount,
  ).errors;
  // RF-28: mandatory — every participant needs ≥1 zone; the step can't be completed otherwise.
  const missing = participantsMissingZone(draft);
  const completeness = missing.length ? ['validation.participantsWithoutZone'] : [];
  return dedupe([...geometry, ...completeness]);
}

/** Participants that still have no signature zone (drives the checklist + who-is-missing indicator). */
export function participantsMissingZone(draft: WizardDraft): WizardParticipant[] {
  const missingIds = new Set(
    participantsWithoutZone(
      draft.participants.map((p) => ({ userId: p.userId })),
      draft.zones.map((z) => ({ userId: z.userId, page: z.page, x: z.x, y: z.y, w: z.w, h: z.h })),
    ),
  );
  return draft.participants.filter((p) => missingIds.has(p.userId));
}

export function stepErrors(step: WizardStep, draft: WizardDraft, limits: WizardLimits): string[] {
  switch (step) {
    case 'pdf':
      return pdfStepErrors(draft, limits);
    case 'participants':
      return participantsStepErrors(draft, limits);
    case 'zones':
      return zonesStepErrors(draft);
    case 'review':
      return [];
  }
}

/** Can we move forward from this step? (zones includes the RF-28 completeness gate.) */
export function canAdvance(step: WizardStep, draft: WizardDraft, limits: WizardLimits): boolean {
  return stepErrors(step, draft, limits).length === 0;
}

/** Send requires EVERY step valid, including mandatory zones (RF-28). */
export function canSend(draft: WizardDraft, limits: WizardLimits): boolean {
  return (
    canAdvance('pdf', draft, limits) &&
    canAdvance('participants', draft, limits) &&
    canAdvance('zones', draft, limits)
  );
}

/** Draft may be incomplete (doc 05 §4.2: "save draft" allows incomplete), but createTransaction
 *  needs at minimum a PDF and a name. */
export function canSaveDraft(draft: WizardDraft): boolean {
  const name = draft.name.trim();
  return !!draft.pdf && name.length > 0 && name.length <= 200; // Name ≤ 200 is a hard schema bound
}

export function nextStep(step: WizardStep): WizardStep {
  const i = WIZARD_STEPS.indexOf(step);
  return WIZARD_STEPS[Math.min(i + 1, WIZARD_STEPS.length - 1)]!;
}

export function prevStep(step: WizardStep): WizardStep {
  const i = WIZARD_STEPS.indexOf(step);
  return WIZARD_STEPS[Math.max(i - 1, 0)]!;
}

/** Build the CreateTransaction input from the draft (doc 04 §3.1/§4). Assumes a PDF exists. */
export function buildCreateInput(draft: WizardDraft): CreateTransactionInput {
  if (!draft.pdf) throw new Error('buildCreateInput called without a PDF');
  const participants = draft.participants.map((p) =>
    draft.routing === 'sequential' && p.order !== undefined ? { userId: p.userId, order: p.order } : { userId: p.userId },
  );
  const input: CreateTransactionInput = {
    Name: draft.name.trim(),
    RoutingType: draft.routing,
    PdfBase64: draft.pdf.base64,
    ParticipantsJson: JSON.stringify(participants),
  };
  const message = draft.message.trim();
  if (message) input.Message = message;
  if (draft.expirationDays !== undefined) input.ExpirationDays = draft.expirationDays;
  if (draft.zones.length) {
    input.ZonesJson = JSON.stringify(
      draft.zones.map((z) => ({ userId: z.userId, page: z.page, x: z.x, y: z.y, w: z.w, h: z.h })),
    );
  }
  return input;
}

function dedupe(xs: string[]): string[] {
  return [...new Set(xs)];
}

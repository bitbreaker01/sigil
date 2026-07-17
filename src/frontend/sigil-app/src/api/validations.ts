// Client-side validations (doc 05 §4.2, EXPLICIT MIRROR of doc 04 §3.4): everything cheap
// is validated BEFORE encoding and uploading 27 MB. The messages come from i18n in the caller;
// here we return keys/structure. The ones only the backend can do (magic bytes, encrypted
// PDF, prior signatures) are NOT here — their errors arrive from the backend and are shown as-is.

import type { ParticipantInput, ZoneInput } from './contracts';

export interface ValidationResult {
  ok: boolean;
  errors: string[]; // i18n keys or messages; the caller resolves them
}

const ok: ValidationResult = { ok: true, errors: [] };

/** PDF size and format BEFORE encoding (doc 04 §3.4: length before decoding). */
export function validatePdf(file: File, maxKb: number): ValidationResult {
  const errors: string[] = [];
  if (!file.name.toLowerCase().endsWith('.pdf') && file.type !== 'application/pdf')
    errors.push('validation.pdfExtension');
  if (file.size > maxKb * 1024)
    errors.push('validation.pdfSize');
  return errors.length ? { ok: false, errors } : ok;
}

/** ParticipantsJson: duplicates, maximum, order 1..N without gaps (sequential), order sequential-only. */
export function validateParticipants(
  participants: ParticipantInput[],
  routing: 'sequential' | 'parallel',
  maxParticipants: number,
): ValidationResult {
  const errors: string[] = [];
  if (participants.length === 0) errors.push('validation.noParticipants');
  if (participants.length > maxParticipants) errors.push('validation.maxParticipants');

  const ids = participants.map((p) => p.userId);
  if (new Set(ids).size !== ids.length) errors.push('validation.duplicateParticipants');
  if (ids.some((id) => !id)) errors.push('validation.participantWithoutId');

  if (routing === 'sequential') {
    if (participants.some((p) => p.order === undefined)) {
      errors.push('validation.sequentialWithoutOrder');
    } else {
      const orders = participants.map((p) => p.order!).sort((a, b) => a - b);
      const consecutive = orders.every((o, i) => o === i + 1);
      if (!consecutive) errors.push('validation.orderWithGaps');
    }
  } else if (participants.some((p) => p.order !== undefined)) {
    errors.push('validation.parallelWithOrder');
  }
  return errors.length ? { ok: false, errors } : ok;
}

/** ZonesJson: valid page, coordinates 0–100, w/h>0, fits the page, userId is a participant. */
export function validateZones(
  zones: ZoneInput[],
  participantUserIds: ReadonlySet<string>,
  pageCount: number,
): ValidationResult {
  const errors: string[] = [];
  for (const z of zones) {
    if (!participantUserIds.has(z.userId)) errors.push('validation.orphanZone');
    if (z.page < 1 || z.page > pageCount) errors.push('validation.zonePage');
    if (z.x < 0 || z.x > 100 || z.y < 0 || z.y > 100) errors.push('validation.zoneOutOfRange');
    if (z.w <= 0 || z.h <= 0 || z.w > 100 || z.h > 100) errors.push('validation.zoneSize');
    else if (z.x + z.w > 100 || z.y + z.h > 100) errors.push('validation.zoneOverflow');
  }
  return errors.length ? { ok: false, errors: [...new Set(errors)] } : ok;
}

/** Completeness RF-28: EVERY participant with ≥1 zone (blocks sending, listing who is missing one). */
export function participantsWithoutZone(
  participants: ParticipantInput[],
  zones: ZoneInput[],
): string[] {
  const withZone = new Set(zones.map((z) => z.userId));
  return participants.filter((p) => !withZone.has(p.userId)).map((p) => p.userId);
}

/** Header: Name ≤200, Message ≤2000, ExpirationDays null or positive (doc 03 §4.1). */
export function validateHeader(name: string, message: string | undefined, expirationDays: number | undefined): ValidationResult {
  const errors: string[] = [];
  if (!name.trim()) errors.push('validation.titleRequired');
  else if (name.length > 200) errors.push('validation.titleTooLong');
  if (message && message.length > 2000) errors.push('validation.messageTooLong');
  if (expirationDays !== undefined && (expirationDays <= 0 || !Number.isInteger(expirationDays)))
    errors.push('validation.invalidExpiration');
  return errors.length ? { ok: false, errors } : ok;
}

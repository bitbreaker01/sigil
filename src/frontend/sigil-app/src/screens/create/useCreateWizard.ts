// Container for the create wizard: holds the draft + current step in React state, exposes
// actions, and calls the seam to submit. All gating/validation is delegated to the PURE model
// (createWizardModel) so this file stays thin. The heavy async (encoding the PDF, reading its
// page count via pdf.js) happens in the PDF step and arrives here as a ready WizardPdf.

import { useCallback, useMemo, useRef, useState } from 'react';
import { sigilApi } from '../../api';
import type { UserSummary } from '../../api/SigilApi';
import type { Routing } from '../../domain/states';
import {
  emptyDraft,
  stepErrors,
  canAdvance,
  canSend,
  canSaveDraft,
  participantsMissingZone,
  nextStep,
  prevStep,
  buildCreateInput,
  DEFAULT_LIMITS,
  type WizardStep,
  type WizardDraft,
  type WizardPdf,
  type WizardParticipant,
  type WizardZone,
  type WizardLimits,
} from './createWizardModel';

export type SubmitState =
  | { phase: 'idle' }
  | { phase: 'submitting' }
  | { phase: 'done'; txId: string; sent: boolean }
  | { phase: 'error'; message: string };

/** Sequential signing order IS the list order — re-derive it on every participants/routing change. */
function normalizeOrders(participants: WizardParticipant[], routing: Routing): WizardParticipant[] {
  return participants.map((p, i) => {
    if (routing === 'sequential') return { ...p, order: i + 1 };
    const { order: _drop, ...rest } = p;
    return rest;
  });
}

export function useCreateWizard(onDone: (txId: string) => void, limits: WizardLimits = DEFAULT_LIMITS) {
  const [draft, setDraft] = useState<WizardDraft>(emptyDraft);
  const [step, setStep] = useState<WizardStep>('pdf');
  const [submit, setSubmit] = useState<SubmitState>({ phase: 'idle' });
  const zoneSeq = useRef(0);

  const patch = useCallback((p: Partial<WizardDraft>) => setDraft((d) => ({ ...d, ...p })), []);

  // ── header / pdf ──
  // pdf/expirationDays are optional: to CLEAR them we omit the key (exactOptionalPropertyTypes
  // forbids assigning `undefined` to an optional property).
  const setPdf = useCallback((pdf: WizardPdf | undefined) => {
    setDraft((d) => {
      if (pdf) return { ...d, pdf };
      const { pdf: _drop, ...rest } = d;
      return rest;
    });
  }, []);
  const setName = useCallback((name: string) => patch({ name }), [patch]);
  const setMessage = useCallback((message: string) => patch({ message }), [patch]);
  const setExpirationDays = useCallback((expirationDays: number | undefined) => {
    setDraft((d) => {
      if (expirationDays !== undefined) return { ...d, expirationDays };
      const { expirationDays: _drop, ...rest } = d;
      return rest;
    });
  }, []);

  // ── participants / routing ──
  const setRouting = useCallback(
    (routing: Routing) => setDraft((d) => ({ ...d, routing, participants: normalizeOrders(d.participants, routing) })),
    [],
  );
  const addParticipant = useCallback((user: UserSummary) => {
    setDraft((d) => {
      if (d.participants.some((p) => p.userId === user.id)) return d; // no duplicates
      const next: WizardParticipant[] = [
        ...d.participants,
        user.email ? { userId: user.id, name: user.name, email: user.email } : { userId: user.id, name: user.name },
      ];
      return { ...d, participants: normalizeOrders(next, d.routing) };
    });
  }, []);
  const removeParticipant = useCallback((userId: string) => {
    setDraft((d) => ({
      ...d,
      participants: normalizeOrders(d.participants.filter((p) => p.userId !== userId), d.routing),
      zones: d.zones.filter((z) => z.userId !== userId), // drop that signer's zones too
    }));
  }, []);
  const moveParticipant = useCallback((userId: string, dir: -1 | 1) => {
    setDraft((d) => {
      const i = d.participants.findIndex((p) => p.userId === userId);
      const j = i + dir;
      if (i < 0 || j < 0 || j >= d.participants.length) return d;
      const arr = [...d.participants];
      [arr[i], arr[j]] = [arr[j]!, arr[i]!];
      return { ...d, participants: normalizeOrders(arr, d.routing) };
    });
  }, []);

  // ── zones ──
  const addZone = useCallback((zone: Omit<WizardZone, 'id'>) => {
    const id = `z${++zoneSeq.current}`;
    setDraft((d) => ({ ...d, zones: [...d.zones, { ...zone, id }] }));
    return id;
  }, []);
  const updateZone = useCallback((id: string, patchZone: Partial<Omit<WizardZone, 'id'>>) => {
    setDraft((d) => ({ ...d, zones: d.zones.map((z) => (z.id === id ? { ...z, ...patchZone } : z)) }));
  }, []);
  const removeZone = useCallback((id: string) => {
    setDraft((d) => ({ ...d, zones: d.zones.filter((z) => z.id !== id) }));
  }, []);

  // ── navigation ──
  const goNext = useCallback(() => setStep((s) => (canAdvance(s, draft, limits) ? nextStep(s) : s)), [draft, limits]);
  const goBack = useCallback(() => setStep((s) => prevStep(s)), []);

  // ── submit ──
  const doSubmit = useCallback(
    async (send: boolean) => {
      setSubmit({ phase: 'submitting' });
      try {
        const txId = await sigilApi.createTransaction(buildCreateInput(draft));
        if (send) await sigilApi.sendTransaction(txId);
        setSubmit({ phase: 'done', txId, sent: send });
        onDone(txId);
      } catch {
        setSubmit({ phase: 'error', message: 'common.genericError' });
      }
    },
    [draft, onDone],
  );
  const submitSend = useCallback(() => {
    if (canSend(draft, limits)) void doSubmit(true);
  }, [draft, limits, doSubmit]);
  const submitDraft = useCallback(() => {
    if (canSaveDraft(draft)) void doSubmit(false);
  }, [draft, doSubmit]);

  // ── derived (recomputed per render; cheap) ──
  const derived = useMemo(
    () => ({
      errors: stepErrors(step, draft, limits),
      canAdvanceStep: canAdvance(step, draft, limits),
      canSend: canSend(draft, limits),
      canSaveDraft: canSaveDraft(draft),
      missingZones: participantsMissingZone(draft),
    }),
    [step, draft, limits],
  );

  return {
    draft,
    step,
    submit,
    limits,
    ...derived,
    setStep,
    goNext,
    goBack,
    setPdf,
    setName,
    setMessage,
    setExpirationDays,
    setRouting,
    addParticipant,
    removeParticipant,
    moveParticipant,
    addZone,
    updateZone,
    removeZone,
    submitSend,
    submitDraft,
  };
}

export type CreateWizard = ReturnType<typeof useCreateWizard>;

// Sign container. Loads the transaction, its participants and zones
// (which zones are MINE vs others) and whether the user has a Master Signature. The document to
// sign is a binary: fetched DIRECTLY and kept in screen-local state, freed on unmount — never in
// the Query cache. "Approve" is gated on a successful render (`rendered`).

import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { sigilApi } from '../../api';
import type { ZoneView } from '../../api/SigilApi';
import { PARTICIPANT_STATE } from '../../domain/states';

type DocState = { phase: 'loading' } | { phase: 'ready'; base64: string } | { phase: 'error' };

export function useSign(txId: string) {
  const qc = useQueryClient();
  const base = useMemo(() => ['sign', txId] as const, [txId]);
  const tx = useQuery({ queryKey: [...base, 'tx'], queryFn: () => sigilApi.getTransaction(txId) });
  const participants = useQuery({ queryKey: [...base, 'participants'], queryFn: () => sigilApi.participantsOf(txId) });
  const zones = useQuery({ queryKey: [...base, 'zones'], queryFn: () => sigilApi.zonesOf(txId) });
  const masterSignature = useQuery({ queryKey: [...base, 'masterSignature'], queryFn: () => sigilApi.getMasterSignature() });

  const [doc, setDoc] = useState<DocState>({ phase: 'loading' });
  const [rendered, setRendered] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [actionError, setActionError] = useState(false);
  const alive = useRef(true);

  // The document (a binary) lives here, not in Query — and is dropped when the screen unmounts.
  useEffect(() => {
    alive.current = true;
    setDoc({ phase: 'loading' });
    setRendered(false);
    sigilApi.getDocumentContent({ Target: txId, DocumentType: 'content' })
      .then((base64) => { if (alive.current) setDoc({ phase: 'ready', base64 }); })
      .catch(() => { if (alive.current) setDoc({ phase: 'error' }); });
    return () => { alive.current = false; };
  }, [txId]);

  // Identity is async in real mode (Entra objectId → systemuserid); currentUser() is empty there.
  const meId = useQuery({ queryKey: ['currentUserId'], queryFn: () => sigilApi.getCurrentUserId() });
  const me = meId.data;
  const parts = useMemo(() => participants.data ?? [], [participants.data]);
  const allZones = useMemo(() => zones.data ?? [], [zones.data]);
  const myParticipant = me ? parts.find((p) => p.userId === me) : undefined;
  // Only the active-turn participant can sign/reject. UI hint — backend enforces.
  const canAct = !!myParticipant && PARTICIPANT_STATE[myParticipant.state] === 'activeTurn';
  const { myZones, otherZones } = useMemo(() => {
    const mine = myParticipant ? allZones.filter((z) => z.participantId === myParticipant.id) : [];
    const mineSet = new Set(mine);
    return { myZones: mine, otherZones: allZones.filter((z: ZoneView) => !mineSet.has(z)) };
  }, [allZones, myParticipant]);

  // After acting, the dashboard lists are stale (this tx left my Pending, moved in Requests/
  // Participations) and the detail view changed — invalidate so they refetch on the way back
  // instead of showing the just-signed request in "pending" until staleTime expires (the delay
  // the user hit). This is independent of the async sealing.
  const invalidateAfterAction = useCallback(() => {
    void qc.invalidateQueries({ queryKey: ['dashboard'] });
    void qc.invalidateQueries({ queryKey: ['detail', txId] });
  }, [qc, txId]);

  const approve = useCallback(async (): Promise<boolean | null> => {
    setSubmitting(true); setActionError(false);
    try {
      const isLast = await sigilApi.submitSignature(txId); // → IsLastSigner
      invalidateAfterAction();
      return isLast;
    } catch {
      setActionError(true);
      return null;
    } finally {
      setSubmitting(false);
    }
  }, [txId, invalidateAfterAction]);

  const reject = useCallback(async (reason: string): Promise<boolean> => {
    setSubmitting(true); setActionError(false);
    try {
      await sigilApi.rejectTransaction({ Target: txId, Reason: reason });
      invalidateAfterAction();
      return true;
    } catch {
      setActionError(true);
      return false;
    } finally {
      setSubmitting(false);
    }
  }, [txId, invalidateAfterAction]);

  return {
    tx: tx.data,
    documentBase64: doc.phase === 'ready' ? doc.base64 : undefined,
    docLoading: doc.phase === 'loading',
    docError: doc.phase === 'error',
    myZones,
    otherZones,
    masterSignature: masterSignature.data?.ImageBase64,
    needsMasterSignature: masterSignature.isSuccess && !masterSignature.data?.ImageBase64,
    loading: tx.isLoading || participants.isLoading || zones.isLoading || masterSignature.isLoading || meId.isLoading,
    notFound: tx.isSuccess && !tx.data,
    rendered,
    markRendered: useCallback(() => setRendered(true), []),
    canAct,
    canApprove: rendered && !submitting && canAct,
    submitting,
    actionError,
    approve,
    reject,
    dismissActionError: useCallback(() => setActionError(false), []),
  };
}

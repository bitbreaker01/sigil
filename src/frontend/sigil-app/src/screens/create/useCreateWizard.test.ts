// Tests of the wizard hook: participant/zone mutations, order normalization, step gating, and
// the two submit paths (send vs save draft) against the mock seam.

import { describe, it, expect, vi } from 'vitest';
import { renderHook, act, waitFor } from '@testing-library/react';
import { useCreateWizard } from './useCreateWizard';
import type { WizardPdf } from './createWizardModel';

const PDF: WizardPdf = { file: new File([new Uint8Array(1024)], 'doc.pdf', { type: 'application/pdf' }), base64: 'JVBERi0=', pageCount: 2 };

function setupFull() {
  const onDone = vi.fn();
  const hook = renderHook(() => useCreateWizard(onDone));
  act(() => {
    hook.result.current.setPdf(PDF);
    hook.result.current.setName('Contract');
    hook.result.current.setRouting('parallel');
    hook.result.current.addParticipant({ id: 'a', name: 'Ana', email: 'ana@x.com' });
    hook.result.current.addParticipant({ id: 'b', name: 'Bruno' });
  });
  act(() => {
    hook.result.current.addZone({ userId: 'a', page: 1, x: 10, y: 10, w: 20, h: 8 });
    hook.result.current.addZone({ userId: 'b', page: 2, x: 10, y: 50, w: 20, h: 8 });
  });
  return { hook, onDone };
}

describe('participants & routing', () => {
  it('ignores duplicate participants', () => {
    const { result } = renderHook(() => useCreateWizard(vi.fn()));
    act(() => {
      result.current.addParticipant({ id: 'a', name: 'Ana' });
      result.current.addParticipant({ id: 'a', name: 'Ana again' });
    });
    expect(result.current.draft.participants).toHaveLength(1);
  });

  it('auto-assigns sequential order by position and clears it for parallel', () => {
    const { result } = renderHook(() => useCreateWizard(vi.fn()));
    act(() => {
      result.current.setRouting('sequential');
      result.current.addParticipant({ id: 'a', name: 'Ana' });
      result.current.addParticipant({ id: 'b', name: 'Bruno' });
    });
    expect(result.current.draft.participants.map((p) => p.order)).toEqual([1, 2]);
    act(() => result.current.moveParticipant('b', -1));
    expect(result.current.draft.participants.map((p) => [p.userId, p.order])).toEqual([['b', 1], ['a', 2]]);
    act(() => result.current.setRouting('parallel'));
    expect(result.current.draft.participants.every((p) => p.order === undefined)).toBe(true);
  });

  it('removing a participant also drops their zones', () => {
    const { hook } = setupFull();
    act(() => hook.result.current.removeParticipant('a'));
    expect(hook.result.current.draft.participants.map((p) => p.userId)).toEqual(['b']);
    expect(hook.result.current.draft.zones.every((z) => z.userId !== 'a')).toBe(true);
  });
});

describe('zones', () => {
  it('adds, updates and removes zones with generated ids', () => {
    const { result } = renderHook(() => useCreateWizard(vi.fn()));
    let id = '';
    act(() => { id = result.current.addZone({ userId: 'a', page: 1, x: 5, y: 5, w: 10, h: 10 }); });
    expect(result.current.draft.zones).toHaveLength(1);
    act(() => result.current.updateZone(id, { x: 42 }));
    expect(result.current.draft.zones[0]!.x).toBe(42);
    act(() => result.current.removeZone(id));
    expect(result.current.draft.zones).toHaveLength(0);
  });
});

describe('step gating', () => {
  it('does not advance past an invalid pdf step', () => {
    const { result } = renderHook(() => useCreateWizard(vi.fn()));
    act(() => result.current.goNext()); // no pdf/name yet
    expect(result.current.step).toBe('pdf');
  });

  it('advances through steps once each is valid and reports missing zones', () => {
    const { hook } = setupFull();
    expect(hook.result.current.canSend).toBe(true);
    expect(hook.result.current.missingZones).toEqual([]);
    act(() => hook.result.current.goNext()); // pdf → participants
    expect(hook.result.current.step).toBe('participants');
  });

  it('canSend is false while a participant is missing a zone', () => {
    const { hook } = setupFull();
    act(() => hook.result.current.removeZone(hook.result.current.draft.zones[0]!.id));
    expect(hook.result.current.canSend).toBe(false);
    expect(hook.result.current.missingZones).toHaveLength(1);
  });
});

describe('submit', () => {
  it('submitSend creates the transaction and sends it', async () => {
    const { hook, onDone } = setupFull();
    act(() => hook.result.current.submitSend());
    await waitFor(() => expect(hook.result.current.submit.phase).toBe('done'));
    if (hook.result.current.submit.phase !== 'done') throw new Error('unexpected');
    expect(hook.result.current.submit.sent).toBe(true);
    expect(onDone).toHaveBeenCalledWith(hook.result.current.submit.txId);
  });

  it('submitDraft creates but does not send', async () => {
    const onDone = vi.fn();
    const { result } = renderHook(() => useCreateWizard(onDone));
    act(() => {
      result.current.setPdf(PDF);
      result.current.setName('Just a draft');
    });
    act(() => result.current.submitDraft());
    await waitFor(() => expect(result.current.submit.phase).toBe('done'));
    if (result.current.submit.phase !== 'done') throw new Error('unexpected');
    expect(result.current.submit.sent).toBe(false);
  });

  it('submitSend is a no-op when the draft cannot be sent', () => {
    const onDone = vi.fn();
    const { result } = renderHook(() => useCreateWizard(onDone));
    act(() => result.current.submitSend()); // empty draft
    expect(result.current.submit.phase).toBe('idle');
    expect(onDone).not.toHaveBeenCalled();
  });
});

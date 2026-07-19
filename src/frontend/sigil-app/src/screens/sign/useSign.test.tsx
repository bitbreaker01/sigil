// Integration test of the sign hook against the mock seam: loads the tx + my zones + document,
// gates approve on render (RF-03), and exercises approve/reject.

import { describe, it, expect, beforeAll } from 'vitest';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { renderHook, waitFor, act } from '@testing-library/react';
import { createElement, type ReactNode } from 'react';
import { useSign } from './useSign';
import { sigilApi } from '../../api';

function wrapper() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return ({ children }: { children: ReactNode }) => createElement(QueryClientProvider, { client }, children);
}

let servicesId = '';
let ndaId = '';
let completedId = '';
beforeAll(async () => {
  const pending = await sigilApi.myPending();
  servicesId = pending.find((p) => p.tx.name === 'Services Agreement 2026')!.tx.id;
  ndaId = pending.find((p) => p.tx.name === 'NDA — Project Falcon')!.tx.id;
  const parts = await sigilApi.myParticipations();
  completedId = parts.find((t) => t.name === 'Employment Offer — R. Diaz')!.id; // I already signed it
});

describe('useSign', () => {
  it('loads the document and separates my zones from the others', async () => {
    const { result } = renderHook(() => useSign(servicesId), { wrapper: wrapper() });
    await waitFor(() => expect(result.current.loading).toBe(false));
    await waitFor(() => expect(result.current.documentBase64).toBeDefined());
    expect(result.current.myZones.length).toBeGreaterThan(0); // I have a seeded zone
    expect(result.current.otherZones.length).toBeGreaterThan(0); // Ana's zone
    expect(result.current.needsMasterSignature).toBe(true); // none seeded
  });

  it('recognizes the caller as the active-turn participant (canAct)', async () => {
    // Regression: identity is resolved asynchronously (getCurrentUserId). If it isn't wired, the
    // caller is never matched to their participant and canAct stays false → "not your turn".
    const { result } = renderHook(() => useSign(ndaId), { wrapper: wrapper() });
    await waitFor(() => expect(result.current.loading).toBe(false));
    expect(result.current.canAct).toBe(true);
  });

  it('gates approve on a successful render (RF-03)', async () => {
    const { result } = renderHook(() => useSign(ndaId), { wrapper: wrapper() });
    await waitFor(() => expect(result.current.loading).toBe(false));
    await waitFor(() => expect(result.current.documentBase64).toBeDefined());
    expect(result.current.canApprove).toBe(false); // not rendered yet
    act(() => result.current.markRendered());
    expect(result.current.canApprove).toBe(true);
  });

  it('blocks acting when it is not my active turn (I already signed)', async () => {
    const { result } = renderHook(() => useSign(completedId), { wrapper: wrapper() });
    await waitFor(() => expect(result.current.loading).toBe(false));
    expect(result.current.canAct).toBe(false); // matched, but my state is 'signed'
    act(() => result.current.markRendered());
    expect(result.current.canApprove).toBe(false); // rendered but not my turn
  });

  it('approve returns IsLastSigner', async () => {
    const { result } = renderHook(() => useSign(ndaId), { wrapper: wrapper() });
    await waitFor(() => expect(result.current.tx).toBeDefined());
    let isLast: boolean | null = null;
    await act(async () => { isLast = await result.current.approve(); });
    expect(typeof isLast).toBe('boolean');
  });

  it('invalidates the dashboard queries after approving (drops it from Pending promptly)', async () => {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const spy = vi.spyOn(client, 'invalidateQueries');
    const w = ({ children }: { children: ReactNode }) => createElement(QueryClientProvider, { client }, children);
    const { result } = renderHook(() => useSign(ndaId), { wrapper: w });
    await waitFor(() => expect(result.current.tx).toBeDefined());
    await act(async () => { await result.current.approve(); });
    expect(spy).toHaveBeenCalledWith({ queryKey: ['dashboard'] });
  });

  it('reject succeeds with a reason', async () => {
    const { result } = renderHook(() => useSign(servicesId), { wrapper: wrapper() });
    await waitFor(() => expect(result.current.tx).toBeDefined());
    let ok = false;
    await act(async () => { ok = await result.current.reject('does not reflect the agreed terms'); });
    expect(ok).toBe(true);
  });
});

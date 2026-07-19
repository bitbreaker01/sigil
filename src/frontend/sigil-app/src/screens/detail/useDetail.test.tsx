// Integration test of the detail hook against the mock seam. Uses a real seeded tx id (fetched
// from myRequests) and checks loading, creator gating, and that cancelling reaches the terminal
// state + records the reason.

import { describe, it, expect, beforeAll } from 'vitest';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { renderHook, waitFor, act } from '@testing-library/react';
import { createElement, type ReactNode } from 'react';
import { useDetail } from './useDetail';
import { terminationReason } from './detailModel';
import { sigilApi } from '../../api';

function wrapper() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return ({ children }: { children: ReactNode }) => createElement(QueryClientProvider, { client }, children);
}

let mineId = '';
let anaId = '';
beforeAll(async () => {
  const requests = await sigilApi.myRequests(); // created by me
  mineId = requests.find((t) => t.name === 'Vendor Contract Q3')!.id;
  const participations = await sigilApi.myParticipations();
  anaId = participations.find((t) => t.name === 'Services Agreement 2026')!.id; // created by Ana
});

describe('useDetail', () => {
  it('loads the transaction, its participants and events', async () => {
    const { result } = renderHook(() => useDetail(mineId), { wrapper: wrapper() });
    await waitFor(() => expect(result.current.loading).toBe(false));
    expect(result.current.tx?.name).toBe('Vendor Contract Q3');
    expect(result.current.participants.length).toBeGreaterThan(0);
    expect(result.current.events.length).toBeGreaterThan(0);
  });

  it('gates creator actions by identity', async () => {
    const mine = renderHook(() => useDetail(mineId), { wrapper: wrapper() });
    // isCreator depends on the async identity (getCurrentUserId) resolving — wait for it.
    await waitFor(() => expect(mine.result.current.isCreator).toBe(true)); // I created it

    const ana = renderHook(() => useDetail(anaId), { wrapper: wrapper() });
    await waitFor(() => expect(ana.result.current.loading).toBe(false));
    expect(ana.result.current.isCreator).toBe(false); // Ana created it
  });

  it('cancelling reaches the cancelled state and records the reason', async () => {
    const { result } = renderHook(() => useDetail(mineId), { wrapper: wrapper() });
    await waitFor(() => expect(result.current.tx).toBeDefined());
    await act(async () => { await result.current.cancel('no longer needed'); });
    await waitFor(() => expect(result.current.tx?.state).toBe(159460008)); // Cancelled
    expect(terminationReason(result.current.events)).toContain('Cancelled by creator');
  });
});

// Integration test of the dashboard hook against the mock seam (a fresh, per-file MockSigilApi
// with the seeded fixture). Verifies the three lists, the first-run signal, the sealing-error
// surfacing, and that retrying a seal moves it out of the error bucket.

import { describe, it, expect, vi } from 'vitest';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { renderHook, waitFor, act } from '@testing-library/react';
import { createElement, type ReactNode } from 'react';
import { useDashboard } from './useDashboard';
import { sigilApi } from '../../api';

function wrapper() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return ({ children }: { children: ReactNode }) => createElement(QueryClientProvider, { client }, children);
}

describe('useDashboard', () => {
  it('loads the three lists and the first-run signal from the seeded mock', async () => {
    const { result } = renderHook(() => useDashboard(), { wrapper: wrapper() });
    await waitFor(() => expect(result.current.loading).toBe(false));

    expect(result.current.pending).toHaveLength(2); // Services + NDA (my active turn)
    expect(result.current.requests).toHaveLength(3); // sealing + error + completed (created by me)
    expect(result.current.participations).toHaveLength(3); // 2 pending + 1 completed (I'm a signer)
    await waitFor(() => expect(result.current.firstRun).toBe(true)); // no master signature seeded
  });

  it('surfaces the sealing-error transaction and detects active sealing', async () => {
    const { result } = renderHook(() => useDashboard(), { wrapper: wrapper() });
    await waitFor(() => expect(result.current.loading).toBe(false));

    expect(result.current.sealingErrors.map((t) => t.name)).toEqual(['Board Resolution 12']);
    expect(result.current.isSealingActive).toBe(true); // Vendor Contract Q3 is sealing
  });

  it('retrying a failed seal moves it out of the error bucket', async () => {
    const { result } = renderHook(() => useDashboard(), { wrapper: wrapper() });
    await waitFor(() => expect(result.current.sealingErrors).toHaveLength(1));
    const errored = result.current.sealingErrors[0]!;
    await act(async () => { await result.current.retrySealing(errored.id); });
    await waitFor(() => expect(result.current.sealingErrors).toHaveLength(0));
  });

  it('surfaces a dismissable error when an action fails', async () => {
    const spy = vi.spyOn(sigilApi, 'retrySealing').mockRejectedValueOnce(new Error('boom'));
    const { result } = renderHook(() => useDashboard(), { wrapper: wrapper() });
    await waitFor(() => expect(result.current.loading).toBe(false));
    await act(async () => { await result.current.retrySealing('whatever'); });
    await waitFor(() => expect(result.current.actionError).toBe(true));
    act(() => result.current.dismissActionError());
    expect(result.current.actionError).toBe(false);
    spy.mockRestore();
  });
});

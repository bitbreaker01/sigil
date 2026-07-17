// Test of the Onboarding logic against the mock: initial load, format rejection,
// successful validation with normalized preview, and failure reasons (small image).

import { describe, it, expect } from 'vitest';
import { renderHook, waitFor, act } from '@testing-library/react';
import { useOnboarding } from './useOnboarding';

function pngFile(bytes: number, name = 'signature.png'): File {
  return new File([new Uint8Array(bytes)], name, { type: 'image/png' });
}

describe('useOnboarding (hook)', () => {
  it('loads the current signature on mount (mock starts without one → ready)', async () => {
    const { result } = renderHook(() => useOnboarding());
    await waitFor(() => expect(result.current.state.phase).toBe('ready'));
    if (result.current.state.phase !== 'ready') throw new Error('unexpected phase');
    expect(result.current.state.currentSignature).toBeUndefined();
  });

  it('rejects a non-PNG file without calling the backend', async () => {
    const { result } = renderHook(() => useOnboarding());
    await waitFor(() => expect(result.current.state.phase).toBe('ready'));
    act(() => result.current.upload(new File([new Uint8Array(300)], 'signature.txt', { type: 'text/plain' })));
    await waitFor(() => expect(result.current.formatError).toBe(true));
    // phase didn't change: still 'ready'
    expect(result.current.state.phase).toBe('ready');
  });

  it('validates a sufficiently large PNG → success with normalized preview', async () => {
    const { result } = renderHook(() => useOnboarding());
    await waitFor(() => expect(result.current.state.phase).toBe('ready'));
    act(() => result.current.upload(pngFile(300)));
    await waitFor(() => expect(result.current.state.phase).toBe('success'));
    if (result.current.state.phase !== 'success') throw new Error('unexpected phase');
    expect(result.current.state.normalized.length).toBeGreaterThan(0);
  });

  it('returns failure reasons when the image is too small', async () => {
    const { result } = renderHook(() => useOnboarding());
    await waitFor(() => expect(result.current.state.phase).toBe('ready'));
    act(() => result.current.upload(pngFile(10)));
    await waitFor(() => expect(result.current.state.phase).toBe('rejected'));
    if (result.current.state.phase !== 'rejected') throw new Error('unexpected phase');
    expect(result.current.state.reasons.length).toBeGreaterThan(0);
  });
});

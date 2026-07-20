// Test of the Onboarding logic against the mock: initial load, format rejection, the editing step,
// successful validation with normalized preview, failure reasons, and backend fault surfacing.

import { describe, it, expect, vi } from 'vitest';
import { renderHook, waitFor, act } from '@testing-library/react';
import { useOnboarding } from './useOnboarding';
import { sigilApi } from '../../api';

function pngFile(bytes = 300, name = 'signature.png'): File {
  return new File([new Uint8Array(bytes)], name, { type: 'image/png' });
}

// The editor hands raw base64; the mock validates by length (>=200 → valid). Real base64 content
// is irrelevant to the mock — only the length gates valid vs "too small".
const BIG_B64 = 'A'.repeat(300);
const SMALL_B64 = 'A'.repeat(10);

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
    expect(result.current.state.phase).toBe('ready'); // phase didn't change
  });

  it('upload goes to EDITING with the raw image (no backend call yet)', async () => {
    const { result } = renderHook(() => useOnboarding());
    await waitFor(() => expect(result.current.state.phase).toBe('ready'));
    act(() => result.current.upload(pngFile()));
    await waitFor(() => expect(result.current.state.phase).toBe('editing'));
    if (result.current.state.phase !== 'editing') throw new Error('unexpected phase');
    expect(result.current.state.source.startsWith('data:image/png;base64,')).toBe(true);
  });

  // NB: the mock (sigilApi) is a module singleton shared across tests, so assert history by DELTA.
  it('applyEdit → PREVIEW (nothing saved yet), then save → success', async () => {
    const { result } = renderHook(() => useOnboarding());
    await waitFor(() => expect(result.current.state.phase).toBe('ready'));
    const before = result.current.history.length;

    act(() => result.current.upload(pngFile()));
    await waitFor(() => expect(result.current.state.phase).toBe('editing'));

    act(() => result.current.applyEdit(BIG_B64));
    await waitFor(() => expect(result.current.state.phase).toBe('preview'));
    const preview = result.current.state;
    if (preview.phase !== 'preview') throw new Error('unexpected phase');
    expect(preview.normalized.length).toBeGreaterThan(0);
    expect(result.current.history).toHaveLength(before); // NOT persisted on validate

    act(() => result.current.save());
    await waitFor(() => expect(result.current.state.phase).toBe('success'));
    const success = result.current.state;
    if (success.phase !== 'success') throw new Error('unexpected phase');
    expect(success.normalized.length).toBeGreaterThan(0);
    await waitFor(() => expect(result.current.history).toHaveLength(before + 1));
    expect(result.current.history[0]!.isActive).toBe(true); // newest first
  });

  it('cancelPreview discards the preview without saving', async () => {
    const { result } = renderHook(() => useOnboarding());
    await waitFor(() => expect(result.current.state.phase).toBe('ready'));
    const before = result.current.history.length;
    act(() => result.current.upload(pngFile()));
    await waitFor(() => expect(result.current.state.phase).toBe('editing'));
    act(() => result.current.applyEdit(BIG_B64));
    await waitFor(() => expect(result.current.state.phase).toBe('preview'));
    act(() => result.current.cancelPreview());
    await waitFor(() => expect(result.current.state.phase).toBe('ready'));
    expect(result.current.history).toHaveLength(before); // nothing was saved
  });

  it('returns failure reasons when the edited image is too small', async () => {
    const { result } = renderHook(() => useOnboarding());
    await waitFor(() => expect(result.current.state.phase).toBe('ready'));
    act(() => result.current.upload(pngFile()));
    await waitFor(() => expect(result.current.state.phase).toBe('editing'));
    act(() => result.current.applyEdit(SMALL_B64));
    await waitFor(() => expect(result.current.state.phase).toBe('rejected'));
    if (result.current.state.phase !== 'rejected') throw new Error('unexpected phase');
    expect(result.current.state.reasons.length).toBeGreaterThan(0);
  });

  it('surfaces the backend fault message when validate throws (e.g. image too big)', async () => {
    const faultMsg = 'La imagen supera el tamaño máximo de carga de 1500 KB.';
    const spy = vi.spyOn(sigilApi, 'validateMasterSignature').mockRejectedValue(new Error(faultMsg));
    const { result } = renderHook(() => useOnboarding());
    await waitFor(() => expect(result.current.state.phase).toBe('ready'));
    act(() => result.current.upload(pngFile()));
    await waitFor(() => expect(result.current.state.phase).toBe('editing'));
    act(() => result.current.applyEdit(BIG_B64));
    await waitFor(() => expect(result.current.state.phase).toBe('error'));
    if (result.current.state.phase !== 'error') throw new Error('unexpected phase');
    expect(result.current.state.message).toBe(faultMsg);
    spy.mockRestore();
  });

  it('falls back to the generic error when the fault has no readable message', async () => {
    const spy = vi.spyOn(sigilApi, 'validateMasterSignature').mockRejectedValue(new Error(''));
    const { result } = renderHook(() => useOnboarding());
    await waitFor(() => expect(result.current.state.phase).toBe('ready'));
    act(() => result.current.upload(pngFile()));
    await waitFor(() => expect(result.current.state.phase).toBe('editing'));
    act(() => result.current.applyEdit(BIG_B64));
    await waitFor(() => expect(result.current.state.phase).toBe('error'));
    if (result.current.state.phase !== 'error') throw new Error('unexpected phase');
    expect(result.current.state.message).toBe('common.genericError');
    spy.mockRestore();
  });
});

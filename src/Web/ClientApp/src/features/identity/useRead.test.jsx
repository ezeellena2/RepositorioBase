import { act, renderHook, waitFor } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { useRead } from './useRead';

const deferred = () => {
  let resolve;
  let reject;
  const promise = new Promise((onResolve, onReject) => {
    resolve = onResolve;
    reject = onReject;
  });
  return { promise, reject, resolve };
};

const page = (item) => ({ items: [item], nextCursor: null });

describe('useRead', () => {
  it('does not start the deferred initial read after cleanup runs first', async () => {
    const load = vi.fn().mockResolvedValue(page('late'));
    const { unmount } = renderHook(() => useRead(load));

    unmount();
    await act(async () => { await Promise.resolve(); });

    expect(load).not.toHaveBeenCalled();
  });

  it('aborts a manual refresh on unmount and ignores its late settlement', async () => {
    const manual = deferred();
    const signals = [];
    const load = vi.fn(({ signal }) => {
      signals.push(signal);
      return load.mock.calls.length === 1 ? Promise.resolve(page('initial')) : manual.promise;
    });
    const { result, unmount } = renderHook(() => useRead(load));
    await waitFor(() => expect(result.current.status).toBe('loaded'));

    let pending;
    act(() => { pending = result.current.refresh('next'); });
    await waitFor(() => expect(load).toHaveBeenCalledTimes(2));

    unmount();
    manual.resolve(page('late'));
    await act(async () => { await pending; });

    expect(signals[1]).toBeInstanceOf(AbortSignal);
    expect(signals[1].aborted).toBe(true);
  });

  it('does not let a captured refresh start another read after unmount', async () => {
    const load = vi.fn().mockResolvedValue(page('initial'));
    const { result, unmount } = renderHook(() => useRead(load));
    await waitFor(() => expect(result.current.status).toBe('loaded'));
    const staleRefresh = result.current.refresh;

    unmount();
    await act(async () => { await staleRefresh('after-unmount'); });

    expect(load).toHaveBeenCalledOnce();
  });

  it('aborts a disabled read and commits no refusal while the hook stays mounted', async () => {
    let observedSignal;
    const load = vi.fn(({ signal }) => {
      observedSignal = signal;
      return new Promise((_resolve, reject) => {
        signal.addEventListener('abort', () => {
          reject({ problem: { code: 'client_failure', status: 0 } });
        }, { once: true });
      });
    });
    const { result, rerender } = renderHook(
      ({ enabled }) => useRead(load, enabled),
      { initialProps: { enabled: true } },
    );
    await waitFor(() => expect(load).toHaveBeenCalledOnce());

    await act(async () => {
      rerender({ enabled: false });
      await Promise.resolve();
    });

    expect(observedSignal).toBeInstanceOf(AbortSignal);
    expect(observedSignal.aborted).toBe(true);
    expect(result.current.status).toBe('idle');
    expect(result.current.problem).toBeNull();
    expect(result.current.data).toBeNull();
  });

  it('clears the previous lifecycle and rejects its captured refresh after disable', async () => {
    const load = vi.fn().mockResolvedValue(page('initial'));
    const { result, rerender } = renderHook(
      ({ enabled }) => useRead(load, enabled),
      { initialProps: { enabled: true } },
    );
    await waitFor(() => expect(result.current.status).toBe('loaded'));
    const staleRefresh = result.current.refresh;

    rerender({ enabled: false });
    await act(async () => { await staleRefresh('after-disable'); });

    expect(load).toHaveBeenCalledOnce();
    expect(result.current.status).toBe('idle');
    expect(result.current.problem).toBeNull();
    expect(result.current.data).toBeNull();
  });

  it('starts a clean lifecycle for a replacement loader and rejects the stale refresh closure', async () => {
    const replacement = deferred();
    const oldLoad = vi.fn(() => oldLoad.mock.calls.length === 1
      ? Promise.resolve(page('old'))
      : Promise.reject({ problem: { code: 'internal_server_error', status: 500 } }));
    const newLoad = vi.fn(() => replacement.promise);
    const { result, rerender } = renderHook(
      ({ load }) => useRead(load),
      { initialProps: { load: oldLoad } },
    );
    await waitFor(() => expect(result.current.status).toBe('loaded'));
    await act(async () => { await result.current.refresh(undefined); });
    expect(result.current.status).toBe('errored');
    expect(result.current.data).toEqual(page('old'));
    const staleRefresh = result.current.refresh;

    rerender({ load: newLoad });
    await waitFor(() => expect(newLoad).toHaveBeenCalledOnce());
    expect(result.current.status).toBe('loading');
    expect(result.current.problem).toBeNull();
    expect(result.current.data).toBeNull();

    await act(async () => { await staleRefresh('old-loader'); });
    replacement.resolve(page('new'));
    await act(async () => { await replacement.promise; });

    expect(oldLoad).toHaveBeenCalledTimes(2);
    await waitFor(() => expect(result.current.status).toBe('loaded'));
    expect(result.current.data).toEqual(page('new'));
  });

  it('ignores an older loader that settles after a newer refresh', async () => {
    const older = deferred();
    const newer = deferred();
    const load = vi.fn(() => {
      if (load.mock.calls.length === 1) return Promise.resolve(page('initial'));
      return load.mock.calls.length === 2 ? older.promise : newer.promise;
    });
    const { result } = renderHook(() => useRead(load));
    await waitFor(() => expect(result.current.status).toBe('loaded'));

    let olderRequest;
    let newerRequest;
    act(() => { olderRequest = result.current.refresh('older'); });
    await waitFor(() => expect(load).toHaveBeenCalledTimes(2));
    act(() => { newerRequest = result.current.refresh('newer'); });
    await waitFor(() => expect(load).toHaveBeenCalledTimes(3));

    newer.resolve(page('newest'));
    await act(async () => { await newerRequest; });
    expect(result.current.data).toEqual(page('newest'));

    older.resolve(page('stale'));
    await act(async () => { await olderRequest; });
    expect(result.current.data).toEqual(page('newest'));
  });

  it('keeps loaded data through a retryable failure and the retry that follows', async () => {
    const retry = deferred();
    const load = vi.fn(() => {
      if (load.mock.calls.length === 1) return Promise.resolve(page('current'));
      if (load.mock.calls.length === 2) {
        return Promise.reject({ problem: { code: 'internal_server_error', status: 500 } });
      }
      return retry.promise;
    });
    const { result } = renderHook(() => useRead(load));
    await waitFor(() => expect(result.current.status).toBe('loaded'));

    await act(async () => { await result.current.refresh(undefined); });

    expect(result.current.status).toBe('errored');
    expect(result.current.problem).toEqual({ code: 'internal_server_error', status: 500 });
    expect(result.current.data).toEqual(page('current'));

    let retryRequest;
    act(() => { retryRequest = result.current.refresh(undefined); });
    await waitFor(() => expect(load).toHaveBeenCalledTimes(3));
    expect(result.current.status).toBe('loading');
    expect(result.current.problem).toBeNull();
    expect(result.current.data).toEqual(page('current'));

    retry.resolve(page('fresh'));
    await act(async () => { await retryRequest; });
    expect(result.current.status).toBe('loaded');
    expect(result.current.data).toEqual(page('fresh'));
  });

  it('clears stale data when a later read is refused with a non-retryable problem', async () => {
    const load = vi.fn(() => load.mock.calls.length === 1
      ? Promise.resolve(page('current'))
      : Promise.reject({ problem: { code: 'permission_denied', status: 403 } }));
    const { result } = renderHook(() => useRead(load));
    await waitFor(() => expect(result.current.status).toBe('loaded'));

    await act(async () => { await result.current.refresh(undefined); });

    expect(result.current.status).toBe('refused');
    expect(result.current.problem).toEqual({ code: 'permission_denied', status: 403 });
    expect(result.current.data).toBeNull();
  });

  it('merges a successful cursor read with current data when requested', async () => {
    const load = vi.fn(({ cursor }) => Promise.resolve(page(cursor ?? 'first')));
    const { result } = renderHook(() => useRead(load));
    await waitFor(() => expect(result.current.status).toBe('loaded'));

    await act(async () => {
      await result.current.refresh('second', (current, loaded) => ({
        ...loaded,
        items: [...current.items, ...loaded.items],
      }));
    });

    expect(result.current.data.items).toEqual(['first', 'second']);
  });
});

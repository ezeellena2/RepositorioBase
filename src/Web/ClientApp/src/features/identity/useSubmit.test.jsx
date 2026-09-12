import { act, renderHook } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { useSubmit } from './useSubmit';

const deferred = () => {
  let resolve;
  let reject;
  const promise = new Promise((onResolve, onReject) => {
    resolve = onResolve;
    reject = onReject;
  });
  return { promise, resolve, reject };
};

afterEach(() => {
  vi.useRealTimers();
});

describe('useSubmit', () => {
  it('keeps a Retry-After problem and the action busy until its one cooldown expires', async () => {
    vi.useFakeTimers();
    const retryProblem = { status: 429, code: 'rate_limit_exceeded', retryAfterSeconds: 2 };
    const action = vi.fn().mockRejectedValue({ problem: retryProblem });
    const { result } = renderHook(() => useSubmit(action));

    await act(async () => { await result.current.submit(); });

    const stableProblem = result.current.problem;
    expect(stableProblem).toBe(retryProblem);
    expect(result.current.isBusy).toBe(true);
    expect(vi.getTimerCount()).toBe(1);

    act(() => { result.current.clearProblem(); });
    expect(result.current.problem).toBe(stableProblem);
    expect(result.current.isBusy).toBe(true);

    act(() => { vi.advanceTimersByTime(1_999); });
    expect(result.current.problem).toBe(stableProblem);
    expect(result.current.isBusy).toBe(true);

    act(() => { vi.advanceTimersByTime(1); });
    expect(result.current.problem).toBeNull();
    expect(result.current.isBusy).toBe(false);
    expect(vi.getTimerCount()).toBe(0);
  });

  it('lets only the newest settlement own state and removes its cooldown timer on unmount', async () => {
    vi.useFakeTimers();
    const older = deferred();
    const newer = deferred();
    const retryProblem = { status: 503, code: 'service_unavailable', retryAfterSeconds: 3 };
    const { result, unmount } = renderHook(() => useSubmit((pending) => pending.promise));

    let olderSubmit;
    let newerSubmit;
    act(() => {
      olderSubmit = result.current.submit(older);
      newerSubmit = result.current.submit(newer);
    });
    await act(async () => {
      newer.reject({ problem: retryProblem });
      await newerSubmit;
    });
    expect(result.current.problem).toBe(retryProblem);
    expect(result.current.isBusy).toBe(true);
    expect(vi.getTimerCount()).toBe(1);

    await act(async () => {
      older.reject({ problem: { status: 409, code: 'invitation_conflict' } });
      await olderSubmit;
    });
    expect(result.current.problem).toBe(retryProblem);
    expect(result.current.isBusy).toBe(true);

    unmount();
    expect(vi.getTimerCount()).toBe(0);
  });

  it.each([undefined, 0, -1, Number.POSITIVE_INFINITY])(
    'does not start a cooldown for a non-positive finite Retry-After value (%s)',
    async (retryAfterSeconds) => {
      vi.useFakeTimers();
      const action = vi.fn().mockRejectedValue({
        problem: { status: 429, code: 'rate_limit_exceeded', retryAfterSeconds },
      });
      const { result } = renderHook(() => useSubmit(action));

      await act(async () => { await result.current.submit(); });

      expect(result.current.isBusy).toBe(false);
      expect(result.current.problem).not.toBeNull();
      expect(vi.getTimerCount()).toBe(0);
    },
  );
});

import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { isRetryable, toProblem } from './api/apiTransport';

/**
 * Owns one cancellable read lifecycle without interpreting the loaded data.
 * Retryable failures preserve stale data; refusals clear it so they cannot look like an empty result.
 */
export function useRead(load, enabled = true) {
  const lifecycle = useMemo(() => ({ enabled, load }), [enabled, load]);
  const [read, setRead] = useState({ lifecycle: null, data: null, problem: null, status: 'loading' });
  const lifecycleAuthority = useRef(null);
  const activeRequest = useRef(null);
  const requestGeneration = useRef(0);

  const invalidate = useCallback(() => {
    requestGeneration.current += 1;
    const current = activeRequest.current;
    activeRequest.current = null;
    current?.controller.abort();
  }, []);

  const refresh = useCallback(async (cursor, merge) => {
    if (lifecycleAuthority.current !== lifecycle || !lifecycle.enabled) return;

    invalidate();
    if (lifecycleAuthority.current !== lifecycle) return;

    const controller = new AbortController();
    const generation = ++requestGeneration.current;
    activeRequest.current = { controller, generation, lifecycle };
    const isCurrent = () => activeRequest.current?.generation === generation
      && activeRequest.current?.lifecycle === lifecycle
      && lifecycleAuthority.current === lifecycle
      && !controller.signal.aborted;

    setRead((current) => ({
      lifecycle,
      data: current.lifecycle === lifecycle ? current.data : null,
      problem: null,
      status: 'loading',
    }));
    try {
      const loaded = await lifecycle.load({ cursor, signal: controller.signal });
      if (!isCurrent()) return;
      setRead((current) => ({
        lifecycle,
        data: typeof merge === 'function' && current.lifecycle === lifecycle && current.data !== null
          ? merge(current.data, loaded)
          : loaded,
        problem: null,
        status: 'loaded',
      }));
    } catch (failure) {
      if (!isCurrent()) return;
      const problem = toProblem(failure);
      const retryable = isRetryable(problem);
      setRead((current) => ({
        lifecycle,
        data: retryable && current.lifecycle === lifecycle ? current.data : null,
        problem,
        status: retryable ? 'errored' : 'refused',
      }));
    } finally {
      if (activeRequest.current?.generation === generation
        && activeRequest.current?.lifecycle === lifecycle) {
        activeRequest.current = null;
      }
    }
  }, [invalidate, lifecycle]);

  useEffect(() => {
    let active = true;
    lifecycleAuthority.current = lifecycle;

    Promise.resolve().then(() => {
      if (!active || lifecycleAuthority.current !== lifecycle || !lifecycle.enabled) return;
      void refresh(undefined);
    });

    return () => {
      active = false;
      if (lifecycleAuthority.current === lifecycle) lifecycleAuthority.current = null;
      invalidate();
    };
  }, [invalidate, lifecycle, refresh]);

  const ownsRead = read.lifecycle === lifecycle;

  return {
    data: ownsRead ? read.data : null,
    problem: ownsRead ? read.problem : null,
    refresh,
    status: !enabled ? 'idle' : (ownsRead ? read.status : 'loading'),
  };
}

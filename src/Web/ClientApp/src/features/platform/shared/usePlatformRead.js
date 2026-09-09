import { useCallback, useEffect, useState } from 'react';

/**
 * One Platform read, held for whichever screen asked for it.
 *
 * It is a read rather than a directory because the retention screen reads a single policy object where the panel
 * reads a page of rows. The hook stores whatever `load` resolves to and never looks inside it, so the same
 * mechanics — load once, reload on demand, show the refusal instead of an empty answer — serve both.
 *
 * `status` names which of those the caller is looking at, because "no rows" and "you were refused" and "not asked
 * yet" are three different screens and `page === null` is the same value in all three. A caller that only renders
 * `page` and `problem` can ignore it.
 */
export function usePlatformRead(load, enabled = true) {
  const [page, setPage] = useState(null);
  const [problem, setProblem] = useState(null);
  const [status, setStatus] = useState('loading');

  const refresh = useCallback(async (cursor) => {
    setStatus('loading');
    try {
      setPage(await load({ cursor }));
      setProblem(null);
      setStatus('loaded');
    } catch (failure) {
      // A read that cannot be completed shows why rather than an empty table, which would read as "there is
      // nothing here" — a very different statement from "you were refused".
      setProblem(failure?.problem ?? { code: 'internal_server_error', status: 0 });
      setPage(null);
      // A failure carrying a problem document is the server refusing in words it authored; one without is
      // anything else that went wrong, and the two are not the same thing to tell somebody.
      setStatus(failure?.problem ? 'refused' : 'errored');
    }
  }, [load]);

  // The first read is loaded inside an async body rather than from the effect directly, so nothing is set
  // synchronously while the component is still rendering. It is not loaded at all while the session still owes
  // its second factor: asking would produce refusals the visitor can do nothing about, and the screen that gates
  // on this already knows the one thing they can do.
  useEffect(() => {
    let cancelled = false;
    (async () => {
      if (!cancelled && enabled) await refresh(undefined);
    })();
    return () => { cancelled = true; };
  }, [refresh, enabled]);

  return { page, problem, refresh, status: enabled ? status : 'idle' };
}

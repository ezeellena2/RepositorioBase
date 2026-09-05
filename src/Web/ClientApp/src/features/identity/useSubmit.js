import { useCallback, useState } from 'react';
import { IdentityProblem } from './api/identityClient';

/**
 * Runs one submission and keeps what the API answered. An unexpected failure is not turned into a problem
 * document: inventing a code the server never sent would put words in its mouth.
 */
export function useSubmit(action) {
  const [problem, setProblem] = useState(null);
  const [isBusy, setIsBusy] = useState(false);
  const [result, setResult] = useState(null);

  const submit = useCallback(async (...args) => {
    setProblem(null);
    setIsBusy(true);
    try {
      const value = await action(...args);
      setResult(value ?? true);
      return value;
    } catch (failure) {
      if (failure instanceof IdentityProblem) setProblem(failure.problem);
      else setProblem({ code: 'internal_server_error', status: 0 });
      return undefined;
    } finally {
      setIsBusy(false);
    }
  }, [action]);

  return { submit, problem, isBusy, result };
}

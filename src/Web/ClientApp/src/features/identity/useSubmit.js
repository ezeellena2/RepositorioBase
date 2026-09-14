import { useCallback, useEffect, useRef, useState } from 'react';
import { toProblem } from '../../api/apiTransport';

/**
 * Runs one submission and keeps the transport's classified answer. Mutations are never replayed here: a timeout
 * leaves their outcome unknown until the user refreshes and decides what to do next.
 */
export function useSubmit(action) {
  const [problem, setProblem] = useState(null);
  const [isBusy, setIsBusy] = useState(false);
  const [result, setResult] = useState(null);
  const mounted = useRef(true);
  const generation = useRef(0);
  const cooldownTimer = useRef(null);
  const coolingDown = useRef(false);

  const cancelCooldown = useCallback(() => {
    if (cooldownTimer.current !== null) clearTimeout(cooldownTimer.current);
    cooldownTimer.current = null;
    coolingDown.current = false;
  }, []);

  useEffect(() => {
    mounted.current = true;
    return () => {
      mounted.current = false;
      generation.current += 1;
      cancelCooldown();
    };
  }, [cancelCooldown]);

  const clearProblem = useCallback(() => {
    if (!mounted.current || coolingDown.current) return;
    setProblem(null);
  }, []);

  const submit = useCallback(async (...args) => {
    const currentGeneration = generation.current + 1;
    generation.current = currentGeneration;
    cancelCooldown();
    if (mounted.current) {
      setProblem(null);
      setIsBusy(true);
    }

    try {
      const value = await action(...args);
      if (!mounted.current || generation.current !== currentGeneration) return value;
      setResult(value ?? true);
      setIsBusy(false);
      return value;
    } catch (failure) {
      if (!mounted.current || generation.current !== currentGeneration) return undefined;

      const nextProblem = toProblem(failure);
      setProblem(nextProblem);
      const retryAfterSeconds = Number(nextProblem.retryAfterSeconds);
      if (Number.isFinite(retryAfterSeconds) && retryAfterSeconds > 0) {
        coolingDown.current = true;
        cooldownTimer.current = setTimeout(() => {
          if (!mounted.current || generation.current !== currentGeneration) return;
          cooldownTimer.current = null;
          coolingDown.current = false;
          setProblem(null);
          setIsBusy(false);
        }, retryAfterSeconds * 1_000);
      } else {
        setIsBusy(false);
      }
      return undefined;
    }
  }, [action, cancelCooldown]);

  return { submit, clearProblem, problem, isBusy, result };
}

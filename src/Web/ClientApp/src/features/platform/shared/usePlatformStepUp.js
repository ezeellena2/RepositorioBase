import { useCallback, useState } from 'react';
import { useIdentity } from '../../identity/context/IdentityProvider';
import { useSubmit } from '../../identity/useSubmit';
import { usePlatformClient } from '../invitations/PlatformInvitationPages';

/**
 * Proving the second factor on a Platform screen that is gated on it.
 *
 * The proof is the server's to confirm, not this form's. So the context is reloaded after the step-up and the
 * factor is re-checked on the reloaded answer: if it is still owed, or the reload came back with nothing, the gate
 * stays up. Reading "it worked" off the request that was just sent is how a screen starts disagreeing with the API
 * about who has proved what.
 *
 * `onProved` is called with no arguments and must close over no refused action. Whatever the operator was denied
 * before the gate appeared is theirs to ask for again, deliberately: an action replayed by the gate that unblocked
 * it is one nobody asked for twice. Keeping the reference out of `onProved` is what makes that structural rather
 * than a rule somebody has to remember.
 */
export function usePlatformStepUp(onProved) {
  const identity = useIdentity();
  const platform = usePlatformClient();
  const [code, setCode] = useState('');
  const { submit, problem, isBusy } = useSubmit(async (value) => {
    await platform.stepUp(value);
    return identity.reload();
  });

  const onSubmit = useCallback(async () => {
    const reloaded = await submit(code);
    // `undefined` is the step-up itself being refused, and `useSubmit` has already kept the problem it answered
    // with. A null context is a reload that did not come back, which says nothing about the factor either way.
    if (reloaded === undefined || reloaded === null) return;
    if (reloaded.session?.requiresTwoFactor === true) return;

    setCode('');
    onProved();
  }, [submit, code, onProved]);

  return { code, onCodeChange: setCode, onSubmit, isBusy, problem };
}

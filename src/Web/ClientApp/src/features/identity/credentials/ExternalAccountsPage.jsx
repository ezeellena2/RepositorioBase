import { useCallback, useEffect, useRef, useState } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { useIdentity } from '../context/IdentityProvider';
import { ProblemMessage } from '../ProblemMessage';
import { externalNavigation } from '../externalNavigation';

/** The providers this application offers. One today; the page is written as a list because that is what it is. */
export const PROVIDERS = [{ id: 'Google', label: 'Google' }];

/**
 * A person's provider accounts (IA-REQ-052, BR-ID-005/006).
 *
 * Linking is never automatic and never implied by a matching address: it is a button this person pressed, in a
 * session this person holds, after proving it is still them. Unlinking asks for the same proof and is refused
 * when it would leave no way back in — which the server decides, because only the server can count.
 */
export function ExternalAccountsPage() {
  const identity = useIdentity();
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const [links, setLinks] = useState(null);
  const [problem, setProblem] = useState(null);
  const [password, setPassword] = useState('');
  const [isBusy, setIsBusy] = useState(false);
  const outcome = params.get('outcome');

  const load = useCallback(async () => {
    try {
      setLinks((await identity.client.listExternalLinks()).items);
      setProblem(null);
    } catch (error) {
      setProblem(error.problem ?? { code: 'unexpected' });
    }
  }, [identity]);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      if (!cancelled) await load();
    })();
    return () => { cancelled = true; };
  }, [load]);

  const run = async (act) => {
    setIsBusy(true);
    setProblem(null);
    try {
      await act();
    } catch (error) {
      setProblem(error.problem ?? { code: 'unexpected' });
    } finally {
      setIsBusy(false);
    }
  };

  const link = (provider) => run(async () => {
    // The proof is bought here, before the browser ever leaves for the provider: somebody who cannot prove it is
    // still them should not reach a consent screen at all.
    await identity.client.reauthenticate('external.link', password);
    setPassword('');
    const { authorizationRequestUri } = await identity.client.startExternalLink(provider);
    externalNavigation.leaveFor(authorizationRequestUri);
  });

  const unlink = (provider) => run(async () => {
    await identity.client.reauthenticate('external.unlink', password);
    setPassword('');
    await identity.client.unlinkExternal(provider);
    await load();
  });

  const linked = (provider) => (links ?? []).find((row) => row.provider === provider);

  return (
    <section aria-labelledby="external-heading">
      <h1 id="external-heading">Sign-in providers</h1>
      <ProblemMessage problem={problem} />
      {outcome === 'linked' && <p role="status">That account is linked. Your other devices have been signed out.</p>}
      {outcome === 'refused' && <p role="alert">That did not complete. Nothing was changed.</p>}
      <p>
        Linking is something you do from here, never something that happens because an address matched. You always
        keep at least one way to sign in.
      </p>

      <label htmlFor="external-password">Password</label>
      <input
        id="external-password"
        type="password"
        autoComplete="current-password"
        value={password}
        onChange={(event) => setPassword(event.target.value)}
      />

      {links === null ? <p role="status">Loading…</p> : (
        <ul>
          {PROVIDERS.map((provider) => {
            const row = linked(provider.id);
            return (
              <li key={provider.id}>
                <span>{provider.label}</span>
                {row ? (
                  <>
                    <span> — {row.providerEmail}</span>
                    <button type="button" disabled={isBusy} onClick={() => unlink(provider.id)}>
                      Unlink {provider.label}
                    </button>
                  </>
                ) : (
                  <button type="button" disabled={isBusy} onClick={() => link(provider.id)}>
                    Link {provider.label}
                  </button>
                )}
              </li>
            );
          })}
        </ul>
      )}
      <button type="button" onClick={() => navigate('/identity')}>Back to your access</button>
    </section>
  );
}

/**
 * Where the provider leg ends. It is a fixed local route, and everything it needs is in the cookie the callback
 * sealed: the query carries one word from a closed set — `signed_in`, `linked`, `proved` or `refused` — and never
 * who the provider said you are. The completion itself is a first-party request with this application's own
 * antiforgery pair, which is why the callback is allowed not to have one.
 */
export function ExternalReturnPage() {
  const identity = useIdentity();
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const [problem, setProblem] = useState(null);
  const outcome = params.get('outcome');

  // The completion spends the handoff, so it has to happen once. A second run would answer
  // `invalid_external_login` against a round trip that already succeeded and show a failure that is not one.
  const settled = useRef(false);

  useEffect(() => {
    let cancelled = false;
    if (settled.current) return undefined;
    settled.current = true;
    (async () => {
      if (outcome !== 'signed_in' && outcome !== 'linked' && outcome !== 'proved') {
        if (!cancelled) navigate('/identity/external?outcome=refused', { replace: true });
        return;
      }

      try {
        await identity.client.completeExternalRoundTrip();
        if (outcome === 'signed_in') {
          // A new session means a new antiforgery pair, exactly as a password sign-in does.
          await identity.client.bootstrapAntiforgery();
        }
        await identity.reload();
        if (!cancelled) navigate(outcome === 'signed_in' ? '/identity' : '/identity/external?outcome=linked', { replace: true });
      } catch (error) {
        if (!cancelled) setProblem(error.problem ?? { code: 'unexpected' });
      }
    })();
    return () => { cancelled = true; };
  }, [identity, navigate, outcome]);

  return (
    <section aria-labelledby="external-return-heading">
      <h1 id="external-return-heading">Finishing up</h1>
      <ProblemMessage problem={problem} />
      {problem === null ? <p role="status">One moment…</p> : <p>Nothing was changed. You can try again.</p>}
    </section>
  );
}

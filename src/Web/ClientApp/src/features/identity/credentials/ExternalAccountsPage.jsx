import { useCallback, useEffect, useRef, useState } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { useIdentity } from '../context/IdentityProvider';
import { forgetPendingProof, markPendingProofProved } from '../useIdentityProof';
import { ProblemMessage } from '../ProblemMessage';
import { externalNavigation } from '../externalNavigation';


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
  const [available, setAvailable] = useState([]);
  const [hasPassword, setHasPassword] = useState(true);
  const [problem, setProblem] = useState(null);
  const [password, setPassword] = useState('');
  const [isBusy, setIsBusy] = useState(false);
  const outcome = params.get('outcome');

  const load = useCallback(async () => {
    try {
      // Both, because what this page may offer depends on both: a link it could remove, and something else to
      // sign in with afterwards. Only the server can count that, so only the server is asked.
      const [listed, credentials] = await Promise.all([
        identity.client.listExternalLinks(),
        identity.client.getOwnCredentials(),
      ]);
      setLinks(listed.items);
      setAvailable(listed.available);
      setHasPassword(credentials.hasPassword);
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
      {/* Only the refusal is taken from the address bar, and only because it claims nothing: it says a round
          trip did not happen. What did happen is never announced from a query parameter — the list below is
          loaded from the server, and it is the only thing on this page that reports a link. */}
      {outcome === 'refused' && <p role="alert">That did not complete. Nothing was changed.</p>}
      <p>
        Linking is something you do from here, never something that happens because an address matched. You always
        keep at least one way to sign in.
      </p>

      {hasPassword && available.length > 0 && (
        <>
          <label htmlFor="external-password">Password</label>
          <input
            id="external-password"
            type="password"
            autoComplete="current-password"
            value={password}
            onChange={(event) => setPassword(event.target.value)}
          />
        </>
      )}

      {links === null ? <p role="status">Loading…</p> : available.length === 0 ? (
        <p>This deployment has no sign-in provider configured, so there is nothing to link here yet.</p>
      ) : (
        <ul>
          {available.map((provider) => {
            const row = linked(provider);

            // The last way in is the server's rule and the server enforces it; saying so here only spares
            // somebody a button whose one possible answer is a refusal.
            const isOnlyWayIn = row !== undefined && !hasPassword && links.length === 1;
            return (
              <li key={provider}>
                <span>{provider}</span>
                {row ? (
                  <>
                    <span> — {row.providerEmail}</span>
                    {isOnlyWayIn ? (
                      <span> — this is your only way to sign in. Set a password before you unlink it.</span>
                    ) : (
                      <button type="button" disabled={isBusy} onClick={() => unlink(provider)}>
                        Unlink {provider}
                      </button>
                    )}
                  </>
                ) : (
                  <button type="button" disabled={isBusy} onClick={() => link(provider)}>
                    Link {provider}
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

  // Cancellation is a ref rather than a closure variable because this effect re-runs when the identity context
  // settles. A per-run flag would be torn down by that re-render while the completion was still in flight,
  // leaving the person on "One moment…" forever with no way to report what happened.
  const mounted = useRef(true);
  useEffect(() => () => { mounted.current = false; }, []);

  useEffect(() => {
    if (settled.current) return;
    settled.current = true;
    (async () => {
      if (outcome !== 'signed_in' && outcome !== 'linked' && outcome !== 'proved') {
        // Refused, cancelled, or an address bar somebody typed. Whatever was waiting is dropped rather than left
        // for a later return to pick up.
        forgetPendingProof();
        if (mounted.current) navigate('/identity/external?outcome=refused', { replace: true });
        return;
      }

      try {
        await identity.client.completeExternalRoundTrip();

        // Always, never conditioned on the slug. The server decides what the round trip did from the cookie it
        // sealed, and a sign-in rotates the pair; deciding from the address bar would leave the transport
        // holding a token whose cookie the server had just deleted, and refuse the very next mutation.
        await identity.client.bootstrapAntiforgery();
        await identity.reload();

        // Only now, once the SERVER accepted the round trip, may what was waiting be allowed to run — and the
        // person goes back to where they were rather than to a screen they never asked for.
        const resumeAt = outcome === 'proved' ? markPendingProofProved() : null;
        const destination = resumeAt ?? (outcome === 'signed_in' ? '/identity' : '/identity/external');
        if (mounted.current) navigate(destination, { replace: true });
      } catch (error) {
        // The completion failed, so nothing was proved and nothing may resume.
        forgetPendingProof();
        if (mounted.current) setProblem(error.problem ?? { code: 'unexpected' });
      }
    })();
  }, [identity, navigate, outcome]);

  return (
    <section aria-labelledby="external-return-heading">
      <h1 id="external-return-heading">Finishing up</h1>
      <ProblemMessage problem={problem} />
      {problem === null ? <p role="status">One moment…</p> : <p>Nothing was changed. You can try again.</p>}
    </section>
  );
}

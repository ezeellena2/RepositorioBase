import { useCallback, useEffect, useRef, useState } from 'react';
import { Link as RouterLink, useNavigate, useSearchParams } from 'react-router-dom';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import CircularProgress from '@mui/material/CircularProgress';
import List from '@mui/material/List';
import ListItem from '@mui/material/ListItem';
import ListItemText from '@mui/material/ListItemText';
import Paper from '@mui/material/Paper';
import Skeleton from '@mui/material/Skeleton';
import Stack from '@mui/material/Stack';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import { useTranslation } from '../../../i18n';
import { toProblem } from '../../../api/apiTransport';
import { useIdentity } from '../context/IdentityProvider';
import { forgetPendingProof, markPendingProofProved } from '../useIdentityProof';
import { ProblemMessage } from '../../../components/ProblemMessage';
import { externalNavigation } from '../externalNavigation';
import { useRead } from '../useRead';

/**
 * One width for the whole screen, chosen rather than inherited. This is a single-object screen — one identity, the
 * handful of providers a deployment offers, and one field — so it takes the single-object cap instead of letting
 * each section stretch to the shell's `lg` container. Everything on the page shares it, which is why the supporting
 * line no longer caps itself at 640 and the password field no longer caps itself at 360: a second cap inside a
 * narrower page is a width nobody chose.
 */
const page = { maxWidth: 560 };

const supporting = { mt: 0.5 };
const section = { p: { xs: 2, sm: 3 } };
const providerRow = { gap: 2, flexWrap: 'wrap', justifyContent: 'space-between' };
const empty = { p: 4, textAlign: 'center' };
const back = { alignSelf: 'flex-start' };
const identityRoute = '/identity';

/** The return leg owns an otherwise empty page, so its one column is centred rather than left against the drawer. */
const returnPage = { maxWidth: 560, mx: 'auto' };
const waiting = { alignItems: 'center' };
const refusal = { alignItems: 'flex-start' };
const secondarySlotProps = { secondary: { component: 'div' } };
const outcomes = {
  refused: 'refused',
  signedIn: 'signed_in',
  linked: 'linked',
  proved: 'proved',
};

/**
 * A person's provider accounts (IA-REQ-052, BR-ID-005/006).
 *
 * Linking is never automatic and never implied by a matching address: it is a button this person pressed, in a
 * session this person holds, after proving it is still them. Unlinking asks for the same proof and is refused
 * when it would leave no way back in — which the server decides, because only the server can count.
 */
export function ExternalAccountsPage() {
  const identity = useIdentity();
  const { t } = useTranslation('identity');
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const [actionProblem, setActionProblem] = useState(null);
  const [actionTarget, setActionTarget] = useState(null);
  const [password, setPassword] = useState('');
  const [isBusy, setIsBusy] = useState(false);
  const outcome = params.get('outcome');

  const read = useRead(useCallback(async ({ signal }) => {
    // Both, because what this page may offer depends on both: a link it could remove, and something else to
    // sign in with afterwards. Only the server can count that, so only the server is asked.
    const [listed, credentials] = await Promise.all([
      identity.client.listExternalLinks({ signal }),
      identity.client.getOwnCredentials({ signal }),
    ]);
    return { ...listed, hasPassword: credentials.hasPassword };
  }, [identity.client]));
  const links = read.data?.items ?? null;
  const available = read.data?.available ?? [];
  const hasPassword = read.data?.hasPassword ?? true;

  const run = async (provider, act) => {
    setIsBusy(true);
    setActionTarget(provider);
    setActionProblem(null);
    try {
      await act();
    } catch (error) {
      setActionProblem(toProblem(error));
    } finally {
      setIsBusy(false);
    }
  };

  const link = (provider) => run(provider, async () => {
    // The proof is bought here, before the browser ever leaves for the provider: somebody who cannot prove it is
    // still them should not reach a consent screen at all.
    await identity.client.reauthenticate('external.link', password);
    setPassword('');
    const { authorizationRequestUri } = await identity.client.startExternalLink(provider);
    externalNavigation.leaveFor(authorizationRequestUri);
  });

  const unlink = (provider) => run(provider, async () => {
    await identity.client.reauthenticate('external.unlink', password);
    setPassword('');
    await identity.client.unlinkExternal(provider);
    await read.refresh(undefined);
  });

  const linked = (provider) => (links ?? []).find((row) => row.provider === provider);

  return (
    <Stack component="section" aria-labelledby="external-heading" spacing={3} sx={page}>
      {/* No action sits beside the title, because this screen has none of its own: everything it can do belongs to
          a provider and is offered on that provider's row. Nothing is promoted here to fill the slot. */}
      <Box>
        <Typography id="external-heading" component="h1" variant="h5">{t('common:navigation.signInProviders')}</Typography>
        <Typography variant="body2" color="text.secondary" sx={supporting}>
          {t('credentials.external.description')}
        </Typography>
      </Box>

      {/* Only the refusal is taken from the address bar, and only because it claims nothing: it says a round
          trip did not happen. What did happen is never announced from a query parameter — the list below is
          loaded from the server, and it is the only thing on this page that reports a link. */}
      {outcome === outcomes.refused && (
        <Alert severity="warning" role="alert">{t('credentials.external.refused')}</Alert>
      )}

      {/* The field is spent on the rows below, so it is given a section of its own rather than left floating on the
          page background between the header and the list. */}
      {hasPassword && available.length > 0 && (
        <Paper variant="outlined" sx={section}>
          <TextField
            id="external-password"
            label={t('login.password')}
            type="password"
            autoComplete="current-password"
            fullWidth
            value={password}
            onChange={(event) => setPassword(event.target.value)}
          />
        </Paper>
      )}

      <ProblemMessage problem={read.problem} />
      {read.status === 'errored' && (
        <Button type="button" variant="outlined" onClick={() => read.refresh(undefined)}>{t('common:actions.tryAgain')}</Button>
      )}

      {read.status === 'loading' && read.data === null ? (
        // The wait holds the shape of the rows that are coming, so the list does not arrive by pushing the page
        // down. The word is what a reader of the status region is told, so it stays — as the region's name rather
        // than as a line of content the rows then have to replace.
        // An entity is literal text inside an attribute, so the character itself is written here.
        <Stack spacing={1} role="status" aria-label={t('credentials.external.loading')}>
          {[0, 1].map((placeholder) => <Skeleton key={placeholder} variant="rounded" height={64} />)}
        </Stack>
      ) : links === null ? null : available.length === 0 ? (
        <Paper variant="outlined" sx={empty}>
          <Typography variant="body2" color="text.secondary">
            {t('credentials.external.empty')}
          </Typography>
        </Paper>
      ) : (
        <Paper variant="outlined">
          <List dense disablePadding>
            {available.map((provider, index) => {
              const row = linked(provider);

              // The last way in is the server's rule and the server enforces it; saying so here only spares
              // somebody a button whose one possible answer is a refusal.
              const isOnlyWayIn = row !== undefined && !hasPassword && links.length === 1;
              return (
                <ListItem key={provider} divider={index < available.length - 1} sx={providerRow}>
                  <ListItemText
                    primary={provider}
                    slotProps={secondarySlotProps}
                    secondary={row === undefined ? null : (
                      <>
                        <Typography variant="body2" color="text.secondary">{row.providerEmail}</Typography>
                        {/* One element holding exactly this sentence, dash and all: it is the refusal the server
                            would give, said in the words this screen has always said it in. */}
                        {isOnlyWayIn && (
                          <Typography component="div" variant="caption" color="text.secondary">
                            {t('credentials.external.onlyWayIn')}
                          </Typography>
                        )}
                      </>
                    )}
                  />
                  {/* Row weight, not page weight: a provider's button is one of several equals down the list, so
                      none of them is the screen's primary action and none of them is filled. */}
                  <Stack spacing={1}>
                    <ProblemMessage
                      problem={actionTarget === provider ? actionProblem : null}
                      autoFocus
                    />
                    {row ? (
                      !isOnlyWayIn && (
                        <Button type="button" variant="outlined" color="error" size="small" disabled={isBusy} onClick={() => unlink(provider)}>
                          {t('credentials.external.unlink', { provider })}
                        </Button>
                      )
                    ) : (
                      <Button type="button" variant="outlined" size="small" disabled={isBusy} onClick={() => link(provider)}>
                        {t('credentials.external.link', { provider })}
                      </Button>
                    )}
                  </Stack>
                </ListItem>
              );
            })}
          </List>
        </Paper>
      )}
      {/* A way out of the screen rather than one of its actions, so it stays at the end and stays quiet. It is
          deliberately still a `type="button"` driving `navigate`: turning it into a link would read better, but
          `role` and `type` are contracts a restyle does not get to change. */}
      <Button type="button" variant="text" sx={back} onClick={() => navigate(identityRoute)}>
        {t('credentials.external.back')}
      </Button>
    </Stack>
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
  const { t } = useTranslation('identity');
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
      if (outcome !== outcomes.signedIn && outcome !== outcomes.linked && outcome !== outcomes.proved) {
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
        const resumeAt = outcome === outcomes.proved ? markPendingProofProved() : null;
        const destination = resumeAt ?? (outcome === outcomes.signedIn ? '/identity' : '/identity/external');
        if (mounted.current) navigate(destination, { replace: true });
      } catch (error) {
        // The completion failed, so nothing was proved and nothing may resume.
        forgetPendingProof();
        if (mounted.current) setProblem(toProblem(error));
      }
    })();
  }, [identity, navigate, outcome]);

  return (
    // A session already exists by the time anyone is here, so this is a screen inside the application, not the
    // centred card of the entrance: the heading belongs to the page and the state belongs to a section of it.
    <Stack component="section" aria-labelledby="external-return-heading" spacing={3} sx={returnPage}>
      <Typography id="external-return-heading" component="h1" variant="h5">{t('credentials.externalReturn.title')}</Typography>
      <ProblemMessage problem={problem} autoFocus />
      <Paper variant="outlined" sx={section}>
        {problem === null ? (
          <Stack direction="row" spacing={2} role="status" sx={waiting}>
            <CircularProgress size={20} />
            <Typography variant="body2">{t('credentials.externalReturn.loading')}</Typography>
          </Stack>
        ) : (
          // The refusal is told once, by the alert above; this says what it left behind. It is a dead end that
          // says "you can try again" and offers nothing to try again with — the control belongs here, but its
          // label would be a user-visible string this screen has never carried, so it is reported not written.
          <Stack spacing={2} sx={refusal}>
            <Typography variant="body2">{t('credentials.externalReturn.refused')}</Typography>
            <Button
              component={RouterLink}
              to={outcome === 'signed_in' ? '/login' : '/identity/external'}
              variant="text"
              sx={back}
            >
              {outcome === outcomes.signedIn
                ? t('credentials.externalReturn.backToSignIn')
                : t('credentials.externalReturn.backToProviders')}
            </Button>
          </Stack>
        )}
      </Paper>
    </Stack>
  );
}

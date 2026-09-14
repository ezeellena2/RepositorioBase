import { useCallback, useEffect, useState } from 'react';
import { Link as RouterLink } from 'react-router-dom';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Link from '@mui/material/Link';
import List from '@mui/material/List';
import ListItem from '@mui/material/ListItem';
import ListItemText from '@mui/material/ListItemText';
import Paper from '@mui/material/Paper';
import Skeleton from '@mui/material/Skeleton';
import Stack from '@mui/material/Stack';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import { Trans, useFormat, useTranslation } from '../../../i18n';
import { toProblem } from '../../../api/apiTransport';
import { useIdentity } from '../context/IdentityProvider';
import { ProblemMessage } from '../../../components/ProblemMessage';
import { useIdentityProof } from '../useIdentityProof';
import { useRead } from '../useRead';

/**
 * A width this screen chooses rather than inherits. It is a hybrid — one proof field and a handful of rows, never
 * a directory — so it takes the single-object measure instead of the container: a device row is a label, a time
 * and its own ending control, which reads at this width, while a full-width container would strand three rows and
 * a password field down the left edge of an empty page. The section below states the same cap on itself, because
 * a form owns its width wherever it is put.
 */
const page = { maxWidth: 560 };
const header = { alignItems: 'flex-start', justifyContent: 'space-between', flexWrap: 'wrap' };
const supporting = { mt: 0.5, maxWidth: 640 };
const proofSection = { p: { xs: 2, sm: 3 }, maxWidth: 560 };
const bulkAction = { width: { xs: '100%', sm: 'auto' } };
const deviceRow = {
  alignItems: { xs: 'flex-start', sm: 'center' },
  flexDirection: { xs: 'column', sm: 'row' },
  gap: { xs: 1, sm: 2 },
  minHeight: 44,
};
const deviceName = { alignItems: 'baseline', flexWrap: 'wrap' };
const deviceAction = { width: { xs: '100%', sm: 'auto' } };
const empty = { p: 4, textAlign: 'center' };

/**
 * A waiting row is the height of the row it stands in for, so the list does not jump when the devices land: a
 * dense `ListItem` is 4px above and below a two-line `ListItemText`, whose multiline margins are 6px each and
 * whose two `body2` lines are 20px each.
 */
const rowHeight = 4 + 6 + 20 + 20 + 6 + 4;
const sessionOperation = {
  revokeOthers: { target: 'all', proofAction: 'sessions.revoke-others', operation: 'revoke-others' },
  revokeOne: { proofAction: 'sessions.revoke-one', operation: 'revoke-one' },
};
const listItemTextSlots = { primary: { component: 'div' } };

/**
 * The devices an identity is signed in on, and the two ways to end one (IA-REQ-049).
 *
 * Ending somebody else's device is a sensitive change, so the page buys a proof first. Which proof depends on
 * what this identity has: a password is typed into a field that is cleared the moment it is used and is never
 * written anywhere, and an identity that arrived through a provider proves the same thing by being sent back to
 * that provider. Either way nothing comes back here — the proof lives on the server (IA-REQ-025, IA-REQ-051).
 */
const SessionsPath = '/identity/sessions';

export function SessionsPage() {
  const { formatDate } = useFormat();
  const { t } = useTranslation('identity');
  const identity = useIdentity();
  const proof = useIdentityProof();
  const [actionProblem, setActionProblem] = useState(null);
  const [actionTarget, setActionTarget] = useState(null);
  const [password, setPassword] = useState('');
  const [isBusy, setIsBusy] = useState(false);

  const read = useRead(useCallback(
    ({ signal }) => identity.client.listSessions({ signal }),
    [identity.client],
  ));
  const sessions = read.data;

  const run = async (target, action, act, intent = null) => {
    setIsBusy(true);
    setActionTarget(target);
    setActionProblem(null);
    try {
      // A provider proof leaves for the provider instead of answering, so there is nothing to do here but stop:
      // what this operation was going to write waits for the round trip to come back. A null action means the
      // proof is already held — which is how a resumed operation re-enters here.
      if (action !== null && !await proof.prove(action, password, intent)) return;
      await act();
      setPassword('');
      await read.refresh(undefined);
    } catch (error) {
      setActionProblem(toProblem(error));
    } finally {
      setIsBusy(false);
    }
  };

  // What this screen left behind before leaving for the provider, once the server has accepted the return. The
  // record is forgotten before the request goes out, so refreshing or replaying the return cannot repeat it; the
  // proof is single-use on the server, which refuses a repeat anyway.
  const waiting = proof.resumable(SessionsPath);
  useEffect(() => {
    if (waiting === null || sessions === null || !proof.isReady) return;
    proof.forget();
    // Started after this effect returns, not during it: the operation sets this screen's busy state as its
    // first act, and doing that inside an effect body is what turns one render into a cascade.
    const resume = (act) => { void Promise.resolve().then(act); };

    if (waiting.operation === 'revoke-others') {
      resume(() => run(sessionOperation.revokeOthers.target, null, () => identity.client.revokeOtherSessions()));
      return;
    }

    // The device has to still be listed, and still be another one. Between leaving and coming back it may have
    // expired, been ended elsewhere, or become the one being used.
    const target = sessions.find((session) => session.sessionRef === waiting.target);
    if (waiting.operation !== 'revoke-one' || target === undefined || target.isCurrent) return;
    resume(() => run(`session:${target.sessionRef}`, null, () => identity.client.revokeSession(target.sessionRef)));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [waiting, sessions, proof.isReady]);

  return (
    <Stack component="section" aria-labelledby="sessions-heading" spacing={3} sx={page}>
      <Stack direction="row" spacing={2} sx={header}>
        <Box>
          <Typography id="sessions-heading" component="h1" variant="h5">{t('common:navigation.yourDevices')}</Typography>
          <Typography variant="body2" color="text.secondary" sx={supporting}>
            {t('sessions.description')}
          </Typography>
        </Box>
        {/* Ending every other device at once is the widest thing this screen can do and the least often wanted, so
            it carries the same weight as the per-row ending control rather than the weight of a primary action.
            Nothing here is `contained`: this screen has no constructive primary action to spend it on. */}
        {proof.canProve && (
          <Stack spacing={1} sx={bulkAction}>
            <ProblemMessage problem={actionTarget === 'all' ? actionProblem : null} autoFocus />
            <Button
              type="button"
              variant="outlined"
              color="error"
              disabled={isBusy || !proof.canBegin(password)}
              sx={bulkAction}
              onClick={() => run(
                sessionOperation.revokeOthers.target,
                sessionOperation.revokeOthers.proofAction,
                () => identity.client.revokeOtherSessions(),
                { returnTo: SessionsPath, operation: sessionOperation.revokeOthers.operation })}
            >
              {t('sessions.endEveryOther')}
            </Button>
          </Stack>
        )}
      </Stack>

      {/* The field is a control, so it is given a container and the container carries the width; the two branches
          beside it are prose about the identity rather than something to fill in, so they stay on the page. */}
      {proof.hasPassword ? (
        <Paper variant="outlined" sx={proofSection}>
          <TextField
            id="sessions-password"
            label={t('login.password')}
            type="password"
            autoComplete="current-password"
            fullWidth
            value={password}
            onChange={(event) => setPassword(event.target.value)}
          />
        </Paper>
      ) : proof.provider !== null ? (
        <Typography variant="body2" color="text.secondary">
          {t('sessions.providerProof', { provider: proof.provider })}
        </Typography>
      ) : (
        // A mailed reset is the one way in that needs no proof, which is exactly why it is the way out of here.
        <Typography variant="body2" color="text.secondary">
          <Trans
            ns="identity"
            i18nKey="sessions.noPassword"
            components={{ setPassword: <Link component={RouterLink} to="/credentials/forgot" /> }}
          />
        </Typography>
      )}

      {/* The rows wait for the proof seam as well as for the list. Showing a device before this screen knows
          what it may offer would render the ending controls twice: once wrong, then again right. */}
      <ProblemMessage problem={read.problem} />
      {read.status === 'errored' && (
        <Button type="button" variant="outlined" sx={deviceAction} onClick={() => read.refresh(undefined)}>
          {t('common:actions.tryAgain')}
        </Button>
      )}
      {read.status === 'loading' && read.data === null ? (
        <Stack spacing={1} role="status" aria-label={t('sessions.loading')}>
          {[0, 1, 2].map((placeholder) => <Skeleton key={placeholder} variant="rounded" height={rowHeight} />)}
        </Stack>
      ) : sessions === null ? null : sessions.length === 0 ? (
        <Paper variant="outlined" sx={empty}>
          <Typography variant="body2" color="text.secondary">{t('sessions.empty')}</Typography>
        </Paper>
      ) : (
        <Paper variant="outlined">
          <List dense disablePadding>
            {sessions.map((session, index) => (
              <ListItem key={session.sessionRef} divider={index < sessions.length - 1} sx={deviceRow}>
                <ListItemText
                  slotProps={listItemTextSlots}
                  primary={(
                    <Stack direction="row" spacing={1} useFlexGap sx={deviceName}>
                      <Typography id={`device-${session.sessionRef}`} variant="body2">{session.deviceLabel}</Typography>
                      {/* Deliberately one element holding exactly this text, and deliberately not a chip: the
                          marker for the device being used is read back with its dash. */}
                      {session.isCurrent && (
                        <Typography variant="caption" color="text.secondary">{t('sessions.current')}</Typography>
                      )}
                    </Stack>
                  )}
                  secondary={t('sessions.lastSeen', { lastSeenAt: formatDate(session.lastSeenAt) })}
                />
                {/* Every one of these is named "End this device" — the name a person reads in the row they are
                    looking at, and the name several callers read back, so it cannot change. What distinguishes
                    them is the description: the row's own device label, which a screen reader announces after the
                    name and which leaves that name untouched. */}
                {!session.isCurrent && proof.canProve && (
                  <Stack spacing={1} sx={deviceAction}>
                    <ProblemMessage
                      problem={actionTarget === `session:${session.sessionRef}` ? actionProblem : null}
                      autoFocus
                    />
                    <Button
                      type="button"
                      variant="outlined"
                      color="error"
                      size="small"
                      aria-describedby={`device-${session.sessionRef}`}
                      disabled={isBusy || !proof.canBegin(password)}
                      sx={deviceAction}
                      onClick={() => run(
                        `session:${session.sessionRef}`,
                        sessionOperation.revokeOne.proofAction,
                        () => identity.client.revokeSession(session.sessionRef),
                        { returnTo: SessionsPath, operation: sessionOperation.revokeOne.operation, target: session.sessionRef })}
                    >
                      {t('sessions.endThis')}
                    </Button>
                  </Stack>
                )}
              </ListItem>
            ))}
          </List>
        </Paper>
      )}
    </Stack>
  );
}

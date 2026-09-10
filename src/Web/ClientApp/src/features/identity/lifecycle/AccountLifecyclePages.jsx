import { useCallback, useEffect, useState } from 'react';
import { Link as RouterLink, useLocation, useNavigate } from 'react-router-dom';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Checkbox from '@mui/material/Checkbox';
import Divider from '@mui/material/Divider';
import FormControlLabel from '@mui/material/FormControlLabel';
import Link from '@mui/material/Link';
import Paper from '@mui/material/Paper';
import Stack from '@mui/material/Stack';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import { useIdentity } from '../context/IdentityProvider';
import { ProblemMessage } from '../ProblemMessage';
import { useFragmentToken } from '../useFragmentToken';
import { useIdentityProof } from '../useIdentityProof';
import { useSubmit } from '../useSubmit';

const AccountPath = '/identity/account';
const DeactivateAction = 'identity.account.deactivate';

/** Required without the asterisk MUI would add, which would rename the field for everything that reads its label. */
const requiredField = { inputLabel: { required: false } };

/**
 * The public entrance card, and only that: `elevation={3}` with the heavier padding is the standard's exception
 * for the single centred card a visitor with no session lands on. The two reactivation screens are that card.
 */
const card = { p: { xs: 3, sm: 4 } };

/** Supporting copy hangs off its title rather than standing as a section of its own. */
const supporting = { mt: 0.5 };
/** The same, capped, because inside the shell the container is wide enough for a paragraph to stop being readable. */
const shellSupporting = { ...supporting, maxWidth: 640 };

/** A single-object screen inside the shell: the frame is the page, and the section below it is a `Paper`. */
const frame = { maxWidth: 560 };
const section = { p: { xs: 2, sm: 3 } };

/** Keeps a submit and a link the width of their own words rather than the width of the column they sit in. */
const atStart = { alignSelf: 'flex-start' };

export function AccountPage() {
  const identity = useIdentity();
  const proof = useIdentityProof();
  const navigate = useNavigate();
  const [password, setPassword] = useState('');
  const [confirmed, setConfirmed] = useState(false);
  const [isBusy, setIsBusy] = useState(false);
  const [problem, setProblem] = useState(null);
  // Identity-wide self-service permissions are authorized by the API, not projected as tenant permissions.
  const allowed = identity.isAuthenticated;

  const deactivate = useCallback(async () => {
    await identity.deactivateAccount(() => navigate('/account/reactivation-request', { replace: true, state: { deactivated: true } }));
  }, [identity, navigate]);

  const run = useCallback(async (act) => {
    setIsBusy(true);
    setProblem(null);
    try { await act(); }
    catch (error) { setProblem(error.problem ?? { code: 'unexpected' }); }
    finally { setPassword(''); setIsBusy(false); }
  }, []);

  const waiting = proof.resumable(AccountPath);
  useEffect(() => {
    if (waiting === null || !proof.isReady) return;
    proof.forget();
    if (!allowed || waiting.action !== DeactivateAction || waiting.operation !== 'deactivate' || waiting.confirmed !== true) return;
    void Promise.resolve().then(() => run(deactivate));
  }, [waiting, proof, allowed, run, deactivate]);

  return (
    // Inside the shell, so the page is a frame and the deactivation is a section of it — not one raised card
    // standing in for the whole screen. The header carries no action: the only one here is destructive and is
    // kept at the foot of the form it belongs to rather than in the slot a primary action would occupy.
    <Stack component="section" aria-labelledby="account-heading" spacing={3} sx={frame}>
      {/* What deactivation costs, and the one thing to settle first, belong to the title: they are the case for
          the single destructive action below, not two sections of their own. */}
      <Box>
        <Typography id="account-heading" component="h1" variant="h5">Your account</Typography>
        <Stack spacing={1} sx={shellSupporting}>
          <Typography variant="body2" color="text.secondary">
            Deactivating ends all your sessions and prevents sign-in. Your memberships stay recorded. To return, request an email link and enter your current password.
          </Typography>
          <Typography variant="body2" color="text.secondary">
            If you are the last administrator or Platform owner, give someone else that responsibility first.
          </Typography>
        </Stack>
      </Box>
      <ProblemMessage problem={problem} />
      {!allowed ? (
        // The refusal stands where the form would have stood, at the weight of a statement rather than of a
        // footnote. It is deliberately not an `Alert`: this screen's one alert belongs to `ProblemMessage`.
        <Paper variant="outlined" sx={section}>
          <Typography variant="body1">You do not have permission to deactivate this account.</Typography>
        </Paper>
      ) : (
        <Paper
          variant="outlined"
          component="form"
          sx={section}
          onSubmit={(event) => {
            event.preventDefault();
            if (!confirmed || !proof.isReady || isBusy) return;
            void run(async () => {
              if (await proof.prove(DeactivateAction, password, { returnTo: AccountPath, operation: 'deactivate', confirmed: true })) await deactivate();
            });
          }}
        >
          <Stack spacing={3}>
            {/* Proving who is asking and acknowledging what it costs are one group; the irreversible button is
                kept clear of them rather than sitting one gap below the checkbox. */}
            <Stack spacing={2}>
              {proof.hasPassword ? (
                <TextField
                  id="account-password"
                  label="Current password"
                  type="password"
                  autoComplete="current-password"
                  required
                  fullWidth
                  slotProps={requiredField}
                  value={password}
                  onChange={(event) => setPassword(event.target.value)}
                />
              ) : (
                <Typography variant="body2">
                  {proof.provider ? `${proof.provider} will confirm it is you.` : 'No proof method is available.'} To return after deactivation, you will need a password. <Link component={RouterLink} to="/credentials/forgot">Set a password</Link>.
                </Typography>
              )}
              <FormControlLabel
                control={<Checkbox checked={confirmed} onChange={(event) => setConfirmed(event.target.checked)} />}
                label="I understand that this ends all my sessions and stops sign-in."
              />
            </Stack>
            <Button
              type="submit"
              variant="contained"
              color="error"
              sx={atStart}
              disabled={isBusy || !confirmed || !proof.isReady || !proof.canBegin(password)}
            >
              Deactivate my account
            </Button>
          </Stack>
        </Paper>
      )}
    </Stack>
  );
}

export function RequestReactivationPage() {
  const identity = useIdentity();
  const location = useLocation();
  const [email, setEmail] = useState('');
  const { submit, problem, isBusy, result } = useSubmit((address) => identity.client.requestAccountReactivation(address));

  return (
    <Paper component="section" elevation={3} aria-labelledby="reactivation-request-heading" sx={card}>
      <Stack spacing={3}>
        <Box>
          <Typography id="reactivation-request-heading" component="h1" variant="h5">Reactivate your account</Typography>
          <Typography variant="body2" color="text.secondary" sx={supporting}>
            Use this if you deactivated your own account. This cannot lift an administrative suspension.
          </Typography>
        </Box>
        {/* The context that brought somebody here retires the moment the request is acknowledged: past tense
            stacked above a fresh acknowledgement reads as two answers to one question. Exactly one of these two
            is ever on screen, which is also what keeps a single `role="status"` on the card. */}
        {location.state?.deactivated && !result && (
          <Alert severity="info" role="status">Your account is deactivated. All your sessions have ended.</Alert>
        )}
        <ProblemMessage problem={problem} />
        {result ? (
          <Alert severity="success" role="status">
            If that account can be reactivated, we have sent a link to its email address. Check the inbox.
          </Alert>
        ) : (
          <Stack component="form" spacing={2} onSubmit={(event) => { event.preventDefault(); submit(email); }}>
            <TextField
              id="reactivation-email"
              label="Email"
              type="email"
              autoComplete="username"
              required
              fullWidth
              slotProps={requiredField}
              value={email}
              onChange={(event) => setEmail(event.target.value)}
            />
            <Button type="submit" variant="contained" size="large" fullWidth disabled={isBusy || identity.isLoading}>
              Send reactivation link
            </Button>
          </Stack>
        )}
        <Divider />
        {/* What the link will ask for once it arrives is a note about the journey above, so it is set as one.
            The way out for somebody who never needed a link at all is an action, and is weighted as one. */}
        <Stack spacing={1}>
          <Typography variant="caption" color="text.secondary">
            Returning requires your current password. <Link component={RouterLink} to="/credentials/forgot">Reset or set a password</Link> if needed, then request a reactivation link.
          </Typography>
          <Link component={RouterLink} to="/login" variant="subtitle2" sx={atStart}>Sign in</Link>
        </Stack>
      </Stack>
    </Paper>
  );
}

export function ReactivateAccountPage() {
  const identity = useIdentity();
  const token = useFragmentToken();
  const [password, setPassword] = useState('');
  const { submit, problem, isBusy, result } = useSubmit(async (chosen) => {
    try { await identity.client.reactivateAccount(token ?? '', chosen); }
    finally { setPassword(''); }
  });

  return (
    <Paper component="section" elevation={3} aria-labelledby="reactivate-heading" sx={card}>
      <Stack spacing={3}>
        <Typography id="reactivate-heading" component="h1" variant="h5">Reactivate your account</Typography>
        <ProblemMessage problem={problem} />
        {result ? (
          // The way on is the whole point of this state, so it is grouped with the answer that produced it and
          // carries an action's weight. It stays a link: a person arriving here signs in on the screen that owns
          // signing in, and that is where this goes.
          <Stack spacing={2}>
            <Alert severity="success" role="status">Your account is active again. Sign in to continue.</Alert>
            <Link component={RouterLink} to="/login" variant="subtitle2" sx={atStart}>Sign in</Link>
          </Stack>
        ) : (
          <>
            {/* A missing token is the reason the form below cannot be used, so it is said at the weight of a
                condition and directly above what it disabled. Its role is neither `alert` nor `status`: both are
                spoken for on this card, by `ProblemMessage` and by the acknowledgement this branch gives way to. */}
            {!token && (
              <Alert severity="warning" role="note">
                Open the link from your reactivation email; this page needs its token.
              </Alert>
            )}
            <Stack component="form" spacing={2} onSubmit={(event) => { event.preventDefault(); submit(password); }}>
              <TextField
                id="reactivate-password"
                label="Current password"
                type="password"
                autoComplete="current-password"
                required
                fullWidth
                slotProps={requiredField}
                value={password}
                onChange={(event) => setPassword(event.target.value)}
              />
              <Button type="submit" variant="contained" size="large" fullWidth disabled={isBusy || identity.isLoading || !token}>
                Reactivate my account
              </Button>
            </Stack>
            <Divider />
            {/* The way back onto the journey when the link did not work, set as the note it is rather than at the
                same weight as the form it sits under. */}
            <Typography variant="caption" color="text.secondary">
              <Link component={RouterLink} to="/credentials/forgot">Reset or set a password</Link> if needed. Then <Link component={RouterLink} to="/account/reactivation-request">request a new reactivation link</Link>.
            </Typography>
          </>
        )}
      </Stack>
    </Paper>
  );
}

import { useState } from 'react';
import { Link as RouterLink, useNavigate } from 'react-router-dom';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import CircularProgress from '@mui/material/CircularProgress';
import Link from '@mui/material/Link';
import Paper from '@mui/material/Paper';
import Stack from '@mui/material/Stack';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import { useIdentity } from '../context/IdentityProvider';
import { ProblemMessage } from '../ProblemMessage';
import { useFragmentToken } from '../useFragmentToken';
import { useSubmit } from '../useSubmit';

/** Required without the asterisk MUI would add, which would rename the field for everything that reads its label. */
const requiredField = { inputLabel: { required: false } };

/**
 * The public entrance card, shared verbatim with LoginPage and ConfirmEmailPage: on those routes the card *is*
 * the page, so it is raised and padded like one. Nothing behind a session is dressed this way.
 */
const card = { p: { xs: 3, sm: 4 } };
/** A section inside the shell is outlined and padded to the section scale, never raised like the card above. */
const section = { p: { xs: 2, sm: 3 } };
/** A single-object screen chooses its width on the page, not on whichever card happens to be standing in it. */
const frame = { maxWidth: 560 };
/** Supporting copy hangs off its title rather than standing as a section of its own. */
const supporting = { mt: 0.5 };
const waiting = { alignItems: 'center' };
const selfStart = { alignSelf: 'flex-start' };

/**
 * Registering from an invitation answers with a neutral bodyless 202 whether the token was live, dead, or
 * already belonged to an account, so this page says the same thing in every case.
 */
export function RegisterFromInvitationPage() {
  const identity = useIdentity();
  const token = useFragmentToken();
  const [password, setPassword] = useState('');
  const { submit, problem, isBusy, result } = useSubmit((secret, chosen) =>
    identity.client.registerFromInvitation(secret, chosen));

  if (result) {
    return (
      <Paper component="section" elevation={3} aria-labelledby="invitation-register-heading" sx={card}>
        <Stack spacing={3}>
          <Typography id="invitation-register-heading" component="h1" variant="h5">Set up your account</Typography>
          <Alert severity="success" role="status">
            Check your email. If that invitation is still open, we have sent you what you need to continue.
          </Alert>
        </Stack>
      </Paper>
    );
  }

  return (
    <Paper component="section" elevation={3} aria-labelledby="invitation-register-heading" sx={card}>
      <Stack spacing={3}>
        <Typography id="invitation-register-heading" component="h1" variant="h5">Set up your account</Typography>
        <ProblemMessage problem={problem} />
        <Stack component="form" spacing={2} onSubmit={(event) => { event.preventDefault(); submit(token ?? '', password); }}>
          <TextField
            id="invitation-password"
            label="Choose a password"
            type="password"
            autoComplete="new-password"
            required
            fullWidth
            slotProps={requiredField}
            value={password}
            onChange={(event) => setPassword(event.target.value)}
          />
          <Button type="submit" variant="contained" size="large" fullWidth disabled={isBusy}>Continue</Button>
        </Stack>
      </Stack>
    </Paper>
  );
}

/**
 * Accepting is idempotent: the same token answers with the same membership, so a retry is safe to offer.
 *
 * This route is answered by a session that already exists, so it is reached inside the application shell and is
 * composed as a screen rather than as an entrance: the title and the precondition it turns on are the page, and
 * the one thing there is to do here — with whatever came of doing it — is a section within it. The raised card is
 * the public entrance's shape one screen earlier, and a screen that borrows it ends up floating in the corner of
 * a container it does not own.
 */
export function AcceptInvitationPage() {
  const identity = useIdentity();
  const navigate = useNavigate();
  const token = useFragmentToken();
  const { submit, problem, isBusy, result } = useSubmit(async (secret) => {
    const accepted = await identity.client.acceptInvitation(secret);
    // reload returns null on a read failure; acceptance still succeeded and must not be submitted again.
    await identity.reload();
    return accepted;
  });

  return (
    <Stack component="section" aria-labelledby="invitation-accept-heading" spacing={3} sx={frame}>
      {/* Being signed in as the invited address is the precondition for the only action here, so it is stated
          with the title rather than left below the button it disabled — and at full weight, because it is the
          reason that button cannot be pressed rather than a remark about the screen. It lives and dies with the
          button: once the invitation has been accepted it is the reason for nothing. */}
      <Box>
        <Typography id="invitation-accept-heading" component="h1" variant="h5">Accept your invitation</Typography>
        {!result && !identity?.isAuthenticated && (
          <Typography id="invitation-accept-precondition" variant="body2" sx={supporting}>
            Sign in with the invited address first.
          </Typography>
        )}
      </Box>

      <Paper variant="outlined" sx={section}>
        <Stack spacing={2}>
          <ProblemMessage problem={problem} />

          {/* One region, three states. The wait is two round trips, so it stands where the outcome is going to
              stand instead of being appended under a button still offering to start it again — which is also
              what keeps the announced wait and the announced outcome from ever being read out together. */}
          {result ? (
            <>
              <Alert severity="success" role="status">You are a member now.</Alert>
              {identity.isAuthenticated ? (
                <Button type="button" variant="contained" onClick={() => navigate('/identity')} sx={selfStart}>
                  Continue
                </Button>
              ) : (
                <>
                  {/* The membership is safe and the way on is a sign-in, so the way on is said first; the refusal
                      that cost the refresh follows as the detail behind it. In the other order the only sentence
                      carrying a way out reads as a footnote to an error box. */}
                  <Typography variant="body1">
                    Your membership was saved, but your access could not be refreshed. <Link component={RouterLink} to="/login">Sign in to continue</Link>.
                  </Typography>
                  <ProblemMessage problem={identity.contextProblem} />
                </>
              )}
            </>
          ) : isBusy ? (
            <Stack direction="row" spacing={1} role="status" sx={waiting}>
              <CircularProgress size={16} />
              <Typography variant="body2">Accepting and refreshing your access…</Typography>
            </Stack>
          ) : (
            <Button
              type="button"
              variant="contained"
              sx={selfStart}
              // The name stays "Accept" — it is how this button is found. What it is described by is where the
              // reason for a disabled button goes without renaming it.
              aria-describedby={identity?.isAuthenticated ? undefined : 'invitation-accept-precondition'}
              disabled={isBusy || !identity?.isAuthenticated}
              onClick={() => submit(token ?? '')}
            >
              Accept
            </Button>
          )}
        </Stack>
      </Paper>
    </Stack>
  );
}

import { useEffect, useRef, useState } from 'react';
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
import { claimedFieldNames, fieldErrorText, selectFieldErrors } from '../../../components/problemFields';
import { ProblemMessage } from '../../../components/ProblemMessage';
import { useFragmentToken } from '../useFragmentToken';
import { useSubmit } from '../useSubmit';
import { Trans, useTranslation } from '../../../i18n';

/** Required without the asterisk MUI would add, which would rename the field for everything that reads its label. */
const requiredField = { inputLabel: { required: false } };
const passwordFieldName = 'password';
const passwordField = [passwordFieldName];
const passwordFieldIds = { password: 'invitation-password' };

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
const emptyToken = '';
const identityRoute = '/identity';

/**
 * Registering from an invitation answers with a neutral bodyless 202 whether the token was live, dead, or
 * already belonged to an account, so this page says the same thing in every case.
 */
export function RegisterFromInvitationPage() {
  const identity = useIdentity();
  const { t } = useTranslation();
  const token = useFragmentToken();
  const passwordInput = useRef(null);
  const [password, setPassword] = useState('');
  const { submit, problem, isBusy, result } = useSubmit((secret, chosen) =>
    identity.client.registerFromInvitation(secret, chosen));
  const passwordErrors = selectFieldErrors(problem, passwordField);
  const claimedPasswordFields = claimedFieldNames(problem, passwordField);

  useEffect(() => {
    if (passwordErrors.password?.length > 0) passwordInput.current?.focus();
  }, [passwordErrors]);

  if (result) {
    return (
      <Paper component="section" elevation={3} aria-labelledby="invitation-register-heading" sx={card}>
        <Stack spacing={3}>
          <Typography id="invitation-register-heading" component="h1" variant="h5">{t('identity:invitations.register.title')}</Typography>
          <Alert severity="success" role="status">
            {t('identity:invitations.register.success')}
          </Alert>
        </Stack>
      </Paper>
    );
  }

  return (
    <Paper component="section" elevation={3} aria-labelledby="invitation-register-heading" sx={card}>
      <Stack spacing={3}>
        <Box>
          <Typography id="invitation-register-heading" component="h1" variant="h5">{t('identity:invitations.register.title')}</Typography>
          {!token && (
            <Typography variant="body2" sx={supporting}>
              {t('identity:invitations.register.missingToken')}
            </Typography>
          )}
        </Box>
        <ProblemMessage
          problem={problem}
          claimedFields={claimedPasswordFields}
          fieldIds={passwordFieldIds}
          autoFocus={claimedPasswordFields.length === 0}
        />
        <Stack component="form" spacing={2} onSubmit={(event) => { event.preventDefault(); submit(token ?? emptyToken, password); }}>
          <TextField
            id="invitation-password"
            inputRef={passwordInput}
            label={t('identity:invitations.register.choosePassword')}
            type="password"
            autoComplete="new-password"
            required
            fullWidth
            slotProps={requiredField}
            error={Boolean(passwordErrors.password)}
            helperText={fieldErrorText(passwordErrors, passwordFieldName, t) || undefined}
            value={password}
            onChange={(event) => setPassword(event.target.value)}
          />
          <Button type="submit" variant="contained" size="large" fullWidth disabled={isBusy || !token}>{t('identity:invitations.register.continue')}</Button>
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
  const { t } = useTranslation();
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
        <Typography id="invitation-accept-heading" component="h1" variant="h5">{t('identity:invitations.accept.title')}</Typography>
        {!result && !identity?.isAuthenticated && (
          <Typography id="invitation-accept-precondition" variant="body2" sx={supporting}>
            {t('identity:invitations.accept.precondition')}
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
              <Alert severity="success" role="status">{t('identity:invitations.accept.success')}</Alert>
              {identity.isAuthenticated ? (
                <Button type="button" variant="contained" onClick={() => navigate(identityRoute)} sx={selfStart}>
                  {t('identity:invitations.accept.continue')}
                </Button>
              ) : (
                <>
                  {/* The membership is safe and the way on is a sign-in, so the way on is said first; the refusal
                      that cost the refresh follows as the detail behind it. In the other order the only sentence
                      carrying a way out reads as a footnote to an error box. */}
                  <Typography variant="body1">
                    <Trans
                      i18nKey="identity:invitations.accept.refreshFailed"
                      components={{ signIn: <Link component={RouterLink} to="/login" /> }}
                    />
                  </Typography>
                  <ProblemMessage problem={identity.contextProblem} />
                </>
              )}
            </>
          ) : isBusy ? (
            <Stack direction="row" spacing={1} role="status" sx={waiting}>
              <CircularProgress size={16} />
              <Typography variant="body2">{t('identity:invitations.accept.loading')}</Typography>
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
              onClick={() => submit(token ?? emptyToken)}
            >
              {t('identity:invitations.accept.submit')}
            </Button>
          )}
        </Stack>
      </Paper>
    </Stack>
  );
}

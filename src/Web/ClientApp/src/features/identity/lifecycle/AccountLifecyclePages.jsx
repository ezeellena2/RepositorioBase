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
import { Trans, useTranslation } from '../../../i18n';

const AccountPath = '/identity/account';
const DeactivateAction = 'identity.account.deactivate';
const DeactivateOperation = 'deactivate';
const ReactivationRequestPath = '/account/reactivation-request';
const ForgotPasswordPath = '/credentials/forgot';
const LoginPath = '/login';
const emptyToken = '';

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
  const { t } = useTranslation();
  const proof = useIdentityProof();
  const navigate = useNavigate();
  const [password, setPassword] = useState('');
  const [confirmed, setConfirmed] = useState(false);
  const [isBusy, setIsBusy] = useState(false);
  const [problem, setProblem] = useState(null);
  // Identity-wide self-service permissions are authorized by the API, not projected as tenant permissions.
  const allowed = identity.isAuthenticated;

  const deactivate = useCallback(async () => {
    await identity.deactivateAccount(() => navigate(ReactivationRequestPath, { replace: true, state: { deactivated: true } }));
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
    if (!allowed || waiting.action !== DeactivateAction || waiting.operation !== DeactivateOperation || waiting.confirmed !== true) return;
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
        <Typography id="account-heading" component="h1" variant="h5">{t('identity:lifecycle.account.title')}</Typography>
        <Stack spacing={1} sx={shellSupporting}>
          <Typography variant="body2" color="text.secondary">
            {t('identity:lifecycle.account.description')}
          </Typography>
          <Typography variant="body2" color="text.secondary">
            {t('identity:lifecycle.account.lastAdministrator')}
          </Typography>
        </Stack>
      </Box>
      <ProblemMessage problem={problem} />
      {!allowed ? (
        // The refusal stands where the form would have stood, at the weight of a statement rather than of a
        // footnote. It is deliberately not an `Alert`: this screen's one alert belongs to `ProblemMessage`.
        <Paper variant="outlined" sx={section}>
          <Typography variant="body1">{t('identity:lifecycle.account.notAllowed')}</Typography>
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
              if (await proof.prove(DeactivateAction, password, { returnTo: AccountPath, operation: DeactivateOperation, confirmed: true })) await deactivate();
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
                  label={t('identity:credentials.change.currentPassword')}
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
                  <Trans
                    i18nKey="identity:lifecycle.account.proofWithPassword"
                    values={{ proof: proof.provider ? t('identity:lifecycle.account.providerProof', { provider: proof.provider }) : t('identity:lifecycle.account.noProofMethod') }}
                    components={{ setPassword: <Link component={RouterLink} to={ForgotPasswordPath} /> }}
                  />
                </Typography>
              )}
              <FormControlLabel
                control={<Checkbox checked={confirmed} onChange={(event) => setConfirmed(event.target.checked)} />}
                label={t('identity:lifecycle.account.confirmDeactivation')}
              />
            </Stack>
            <Button
              type="submit"
              variant="contained"
              color="error"
              sx={atStart}
              disabled={isBusy || !confirmed || !proof.isReady || !proof.canBegin(password)}
            >
              {t('identity:lifecycle.account.deactivate')}
            </Button>
          </Stack>
        </Paper>
      )}
    </Stack>
  );
}

export function RequestReactivationPage() {
  const identity = useIdentity();
  const { t } = useTranslation();
  const location = useLocation();
  const [email, setEmail] = useState('');
  const { submit, problem, isBusy, result } = useSubmit((address) => identity.client.requestAccountReactivation(address));

  return (
    <Paper component="section" elevation={3} aria-labelledby="reactivation-request-heading" sx={card}>
      <Stack spacing={3}>
        <Box>
          <Typography id="reactivation-request-heading" component="h1" variant="h5">{t('identity:login.reactivateAccount')}</Typography>
          <Typography variant="body2" color="text.secondary" sx={supporting}>
            {t('identity:lifecycle.request.description')}
          </Typography>
        </Box>
        {/* The context that brought somebody here retires the moment the request is acknowledged: past tense
            stacked above a fresh acknowledgement reads as two answers to one question. Exactly one of these two
            is ever on screen, which is also what keeps a single `role="status"` on the card. */}
        {location.state?.deactivated && !result && (
          <Alert severity="info" role="status">{t('identity:lifecycle.request.deactivated')}</Alert>
        )}
        <ProblemMessage problem={problem} />
        {result ? (
          <Alert severity="success" role="status">
            {t('identity:lifecycle.request.success')}
          </Alert>
        ) : (
          <Stack component="form" spacing={2} onSubmit={(event) => { event.preventDefault(); submit(email); }}>
            <TextField
              id="reactivation-email"
              label={t('identity:login.email')}
              type="email"
              autoComplete="username"
              required
              fullWidth
              slotProps={requiredField}
              value={email}
              onChange={(event) => setEmail(event.target.value)}
            />
            <Button type="submit" variant="contained" size="large" fullWidth disabled={isBusy || identity.isLoading}>
              {t('identity:lifecycle.request.submit')}
            </Button>
          </Stack>
        )}
        <Divider />
        {/* What the link will ask for once it arrives is a note about the journey above, so it is set as one.
            The way out for somebody who never needed a link at all is an action, and is weighted as one. */}
        <Stack spacing={1}>
          <Typography variant="caption" color="text.secondary">
            <Trans
              i18nKey="identity:lifecycle.request.returning"
              components={{ resetPassword: <Link component={RouterLink} to={ForgotPasswordPath} /> }}
            />
          </Typography>
          <Link component={RouterLink} to={LoginPath} variant="subtitle2" sx={atStart}>{t('identity:login.submit')}</Link>
        </Stack>
      </Stack>
    </Paper>
  );
}

export function ReactivateAccountPage() {
  const identity = useIdentity();
  const { t } = useTranslation();
  const token = useFragmentToken();
  const [password, setPassword] = useState('');
  const { submit, problem, isBusy, result } = useSubmit(async (chosen) => {
    try { await identity.client.reactivateAccount(token ?? emptyToken, chosen); }
    finally { setPassword(''); }
  });

  return (
    <Paper component="section" elevation={3} aria-labelledby="reactivate-heading" sx={card}>
      <Stack spacing={3}>
        <Typography id="reactivate-heading" component="h1" variant="h5">{t('identity:login.reactivateAccount')}</Typography>
        <ProblemMessage problem={problem} />
        {result ? (
          // The way on is the whole point of this state, so it is grouped with the answer that produced it and
          // carries an action's weight. It stays a link: a person arriving here signs in on the screen that owns
          // signing in, and that is where this goes.
          <Stack spacing={2}>
            <Alert severity="success" role="status">{t('identity:lifecycle.reactivate.success')}</Alert>
            <Link component={RouterLink} to={LoginPath} variant="subtitle2" sx={atStart}>{t('identity:login.submit')}</Link>
          </Stack>
        ) : (
          <>
            {/* A missing token is the reason the form below cannot be used, so it is said at the weight of a
                condition and directly above what it disabled. Its role is neither `alert` nor `status`: both are
                spoken for on this card, by `ProblemMessage` and by the acknowledgement this branch gives way to. */}
            {!token && (
              <Alert severity="warning" role="note">
                {t('identity:lifecycle.reactivate.missingToken')}
              </Alert>
            )}
            <Stack component="form" spacing={2} onSubmit={(event) => { event.preventDefault(); submit(password); }}>
              <TextField
                id="reactivate-password"
                label={t('identity:credentials.change.currentPassword')}
                type="password"
                autoComplete="current-password"
                required
                fullWidth
                slotProps={requiredField}
                value={password}
                onChange={(event) => setPassword(event.target.value)}
              />
              <Button type="submit" variant="contained" size="large" fullWidth disabled={isBusy || identity.isLoading || !token}>
                {t('identity:lifecycle.reactivate.submit')}
              </Button>
            </Stack>
            <Divider />
            {/* The way back onto the journey when the link did not work, set as the note it is rather than at the
                same weight as the form it sits under. */}
            <Typography variant="caption" color="text.secondary">
              <Trans
                i18nKey="identity:lifecycle.reactivate.nextSteps"
                components={{
                  resetPassword: <Link component={RouterLink} to={ForgotPasswordPath} />,
                  requestReactivation: <Link component={RouterLink} to={ReactivationRequestPath} />,
                }}
              />
            </Typography>
          </>
        )}
      </Stack>
    </Paper>
  );
}

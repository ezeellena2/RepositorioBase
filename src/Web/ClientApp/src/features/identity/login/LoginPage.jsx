import { useState } from 'react';
import { Link as RouterLink, Navigate, useLocation, useSearchParams } from 'react-router-dom';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Divider from '@mui/material/Divider';
import Link from '@mui/material/Link';
import Paper from '@mui/material/Paper';
import Stack from '@mui/material/Stack';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import { toProblem } from '../../../api/apiTransport';
import { useIdentity } from '../context/IdentityProvider';
import { ProblemMessage } from '../../../components/ProblemMessage';
import { useSubmit } from '../useSubmit';
import { externalNavigation } from '../externalNavigation';
import { useTranslation } from '../../../i18n';

/**
 * Where a visitor lands after signing in is taken from the query string, so it is treated as untrusted input: a
 * value that is not a same-origin path is discarded rather than followed, which is what stops a crafted link
 * from bouncing someone off-origin the moment they hold a session.
 *
 * The question is asked of the URL parser rather than of the spelling, because spelling rules lose. A leading
 * `//` is the obvious protocol-relative form, but for a special scheme the parser also treats a backslash as a
 * separator — so `/\evil.test` reads as a path here and resolves to `https://evil.test/` there. Resolving the
 * candidate against this origin and comparing the result is the only check that cannot be spelled around.
 */
export function safeReturnUrl(candidate) {
  if (typeof candidate !== 'string' || !candidate.startsWith('/')) return '/';
  try {
    const origin = window.location.origin;
    return new URL(candidate, origin).origin === origin ? candidate : '/';
  } catch {
    // An unparseable candidate is not a destination.
    return '/';
  }
}

/**
 * The required inputs carry the attribute without the label decoration MUI would add for it. The asterisk is a
 * change of accessible name — `Email` becomes `Email *` — and the label is what every caller, person and test
 * alike, identifies the field by.
 */
const requiredField = { inputLabel: { required: false } };

/**
 * Google's own multicolour "G", as its sign-in branding asks for. It is Google's mark rather than a colour of this
 * product's palette, so it is drawn with Google's fills while the button around it stays the theme's. It is hidden
 * from assistive technology: the button's name is still its text alone.
 */
const googleMark = (
  <svg viewBox="0 0 48 48" width="18" height="18" aria-hidden focusable="false">
    <path fill="#EA4335" d="M24 9.5c3.54 0 6.71 1.22 9.21 3.6l6.85-6.85C35.9 2.38 30.47 0 24 0 14.62 0 6.51 5.38 2.56 13.22l7.98 6.19C12.43 13.72 17.74 9.5 24 9.5z" />
    <path fill="#4285F4" d="M46.98 24.55c0-1.57-.15-3.09-.38-4.55H24v9.02h12.94c-.58 2.96-2.26 5.48-4.78 7.18l7.73 6c4.51-4.18 7.09-10.36 7.09-17.65z" />
    <path fill="#FBBC05" d="M10.53 28.59c-.48-1.45-.76-2.99-.76-4.59s.27-3.14.76-4.59l-7.98-6.19C.92 16.46 0 20.12 0 24c0 3.88.92 7.54 2.56 10.78l7.97-6.19z" />
    <path fill="#34A853" d="M24 48c6.48 0 11.93-2.13 15.89-5.81l-7.73-6c-2.15 1.45-4.92 2.3-8.16 2.3-6.26 0-11.57-4.22-13.47-9.91l-7.98 6.19C6.51 42.62 14.62 48 24 48z" />
  </svg>
);

const card = { p: { xs: 3, sm: 4 } };
const alertSeverity = 'error';

export function LoginPage() {
  const identity = useIdentity();
  const { t } = useTranslation('identity');
  const location = useLocation();
  const [params] = useSearchParams();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const returnUrl = params.has('returnUrl')
    ? safeReturnUrl(params.get('returnUrl'))
    : '/identity';
  const { submit, problem, isBusy } = useSubmit((...args) => identity.signIn(...args));
  const [providerProblem, setProviderProblem] = useState(null);
  const [refused, setRefused] = useState(false);

  if (identity?.isAuthenticated) return <Navigate to={returnUrl} replace state={{ from: location }} />;

  /**
   * A refused sign-in is not a failed request. The server answers every refusal a stranger can provoke with the
   * same neutral `204` — a wrong password, a locked account and a deactivated one are indistinguishable on
   * purpose (SPEC section 6) — so nothing is thrown and no problem document arrives. What separates the two
   * outcomes is whether a session now exists, which is exactly what the context read that follows reports:
   * `signIn` hands back the loaded context, or `null` when there was none to load.
   *
   * Saying so is this page's job rather than the context's. The provider records `authentication_required` for
   * nobody, because it is what the endpoint answers every visitor who has yet to sign in, and printing it put a
   * refusal at the top of the card before anybody had asked for anything. Somebody who just pressed the button
   * did ask, and is owed an answer — as neutral as the server's, because this page knows no more than it does.
   */
  const signIn = async () => {
    setRefused(false);
    if (await submit(email, password) === null) setRefused(true);
  };

  // Nothing is chosen for the visitor here: pressing this asks the server where to go, and the account it ends
  // up at is the one that provider account is linked to — never one that merely shares an address.
  const continueWithGoogle = async () => {
    setProviderProblem(null);
    try {
      const { authorizationRequestUri } = await identity.client.startExternalLogin('Google');
      externalNavigation.leaveFor(authorizationRequestUri);
    } catch (error) {
      setProviderProblem(toProblem(error));
    }
  };

  return (
    <Paper component="section" elevation={3} aria-labelledby="login-heading" sx={card}>
      <Stack spacing={3}>
        {/* The two probes are the return URL this card resolved and whatever the context read refused with. They
            are read back by their test ids, so they stay in the markup and out of the reading order. */}
        <Box>
          <Typography id="login-heading" component="h1" variant="h5">{t('login.title')}</Typography>
          <p data-testid="return-url" hidden>{returnUrl}</p>
          <p data-testid="context-problem" hidden>{identity?.contextProblem?.code ?? ''}</p>
        </Box>

        <ProblemMessage problem={problem ?? providerProblem ?? identity?.contextProblem} />
        {refused && problem === null && providerProblem === null && !identity?.contextProblem && (
          <Alert severity={alertSeverity}>{t('login.refused')}</Alert>
        )}

        {/* Two ways in, in the order they are chosen: the provider round trip first, then the credentials this
            card can take itself. The rule dividing them is what makes them read as alternatives. */}
        <Button type="button" variant="outlined" size="large" fullWidth startIcon={googleMark} onClick={continueWithGoogle}>
          {t('login.continueWithGoogle')}
        </Button>
        <Divider />

        <Stack
          component="form"
          spacing={2}
          onSubmit={(event) => { event.preventDefault(); void signIn(); }}
        >
          <TextField
            id="login-email"
            label={t('login.email')}
            type="email"
            autoComplete="username"
            required
            fullWidth
            slotProps={requiredField}
            value={email}
            onChange={(event) => setEmail(event.target.value)}
          />
          <TextField
            id="login-password"
            label={t('login.password')}
            type="password"
            autoComplete="current-password"
            required
            fullWidth
            slotProps={requiredField}
            value={password}
            onChange={(event) => setPassword(event.target.value)}
          />
          <Button type="submit" variant="contained" size="large" fullWidth disabled={isBusy}>{t('login.submit')}</Button>
        </Stack>

        <Divider />

        {/* Neither of these signs anybody in, so they sit below the rule rather than beside the submit. */}
        <Stack spacing={1}>
          <Link component={RouterLink} to="/credentials/forgot" variant="body2">{t('login.forgotPassword')}</Link>
          <Link component={RouterLink} to="/account/reactivation-request" variant="body2">{t('login.reactivateAccount')}</Link>
        </Stack>
      </Stack>
    </Paper>
  );
}

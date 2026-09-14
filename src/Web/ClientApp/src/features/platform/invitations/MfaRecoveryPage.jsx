import { useEffect, useRef, useState } from 'react';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import List from '@mui/material/List';
import ListItem from '@mui/material/ListItem';
import ListItemText from '@mui/material/ListItemText';
import Paper from '@mui/material/Paper';
import Stack from '@mui/material/Stack';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import { useIdentity } from '../../identity/context/IdentityProvider';
import { usePlatformClient } from './PlatformInvitationPages';
import { fieldError, firstInvalid } from '../../../components/problemFields';
import { ProblemMessage } from '../../../components/ProblemMessage';
import { useSubmit } from '../../identity/useSubmit';
import { useTranslation } from '../../../i18n';

/** Required without the asterisk MUI would add, which would rename the field for everything that reads its label. */
const requiredField = { inputLabel: { required: false } };

/**
 * This route is behind a session, so it is a screen inside the shell and not a public entrance: the heading is
 * the page's, the form and the replacement are sections under it, and the width is chosen here. It used to be
 * one `elevation={3}` card with the h1 inside — the entrance card's composition, on a route nobody reaches
 * without signing in first, which left the page with no header at all because the section had swallowed it.
 */
const frame = { maxWidth: 560 };
const section = { p: { xs: 2, sm: 3 } };
const supporting = { mt: 0.5, maxWidth: 640 };

/** An empty state is a centred block in a container of its own. */
const empty = { p: 4, textAlign: 'center' };

/**
 * The definition list a single shown-once value is stated in, as a label/value pair rather than a sentence — the
 * same constant, meaning the same thing, as the one in PlatformInvitationPages.jsx. It was a hand-rolled two
 * column CSS grid here and a plain `dl` there: one name, two layouts, for one domain object.
 */
const facts = { m: 0 };
const value = { m: 0, mt: 0.5 };

/** Both shown-once values are set out the same way: a bordered block holding only what has to be copied down. */
const codeBlock = { px: 2, py: 1 };
const leading = { alignSelf: 'flex-start' };
const recoveryHeadingId = 'mfa-recovery-heading';
const recoveryAction = 'platform.mfa.recover';
const emptyFieldValue = '';
const recoveryClaimedFields = ['password', 'recoveryCode'];

/**
 * Getting a second factor back after losing the authenticator that held it (IA-REQ-041, C6).
 *
 * The page takes two things and stores neither: the password, re-typed to buy a proof for this action alone, and
 * one recovery code. Both leave the browser once. What comes back — a shared key and a new set of codes — is
 * shown once and is never readable again, here or anywhere else, so the page says so rather than letting somebody
 * assume they can come back for it (IA-REQ-025).
 *
 * There is no token in the URL, unlike the enrollment ceremony: the invitation was consumed at activation, and
 * what authorizes this is the caller's own session plus the code.
 */
export function MfaRecoveryPage() {
  const { t } = useTranslation('platform');
  const identity = useIdentity();
  const platform = usePlatformClient();
  const passwordInput = useRef(null);
  const recoveryCodeInput = useRef(null);
  const [password, setPassword] = useState('');
  const [recoveryCode, setRecoveryCode] = useState('');
  const [replacement, setReplacement] = useState(null);
  const { submit, clearProblem, problem, isBusy } = useSubmit(async (action) => action());
  const invalidField = firstInvalid(problem, ['recoveryCode', 'password']);
  const passwordError = fieldError(problem, 'password', t);
  const recoveryCodeError = fieldError(problem, 'recoveryCode', t);
  const invalidCredentialProof = problem?.code === 'invalid_credential_proof';
  const invalidRecoveryCodeDirect = problem?.code === 'invalid_recovery_code';
  const invalidPassword = passwordError.error || invalidCredentialProof;
  const invalidRecoveryCode = recoveryCodeError.error || invalidRecoveryCodeDirect;
  const directFieldProblem = invalidCredentialProof || invalidRecoveryCodeDirect;

  useEffect(() => {
    if (invalidRecoveryCode) recoveryCodeInput.current?.focus();
    else if (invalidPassword) passwordInput.current?.focus();
  }, [invalidPassword, invalidRecoveryCode]);

  const signedIn = Boolean(identity?.isAuthenticated);
  const asking = signedIn && replacement === null;

  return (
    <Stack component="section" aria-labelledby={recoveryHeadingId} spacing={3} sx={frame}>
      <Box>
        <Typography id={recoveryHeadingId} component="h1" variant="h5">{t('mfa.recovery.title')}</Typography>
        {/* The line that says what this screen is for belongs to the screen, so it sits with the title rather
            than inside the form. It is said while there is still a decision to make and not after. */}
        {asking && (
          <Typography variant="body2" color="text.secondary" sx={supporting}>
            {t('mfa.recovery.description')}
          </Typography>
        )}
      </Box>

      {!signedIn ? (
        /* Signing in is what resolves this state and the visitor can do it, so the empty state offers the door
           rather than only naming it. The sentence stays the whole text of one element — it is read back in one
           piece — so the way out is its sibling and never a link inside it. */
        <Paper variant="outlined" sx={empty}>
          <Typography variant="body2" color="text.secondary">
            {t('mfa.recovery.signInFirst')}
          </Typography>
          {/* The way out belongs here and /login is it, but its label would be a user-visible string this screen
              has never carried, so it is reported rather than written into a visual change. */}
        </Paper>
      ) : replacement ? (
        <>
          <Alert severity="success" role="status">
            {t('mfa.recovery.replacementReady')}
          </Alert>
          <Paper variant="outlined" sx={section}>
            <Stack spacing={2}>
              <Box component="dl" sx={facts}>
                <Typography component="dt" variant="body2" color="text.secondary">{t('mfa.sharedKey')}</Typography>
                <Box component="dd" sx={value}>
                  {/* Given the weight of a value somebody copies by hand, and deliberately nothing else: the
                      element's own text is read and decoded, so a space, a hyphen or a control sharing it would
                      corrupt the key quietly instead of failing loudly. */}
                  <Paper variant="outlined" sx={codeBlock}>
                    <Typography component="div" variant="subtitle1" data-testid="recovered-shared-key">
                      {replacement.sharedKey}
                    </Typography>
                  </Paper>
                </Box>
              </Box>
              {/* The same domain object as the enrollment ceremony's codes, so it is the same composition: a
                  dense list in a bordered block. It was a `ul` with the bullets switched off by hand here and a
                  `List` there — the component MUI already ships carries that reset and its density. */}
              <Paper variant="outlined" sx={codeBlock}>
                <List dense disablePadding aria-label={t('mfa.recoveryCodes')}>
                  {replacement.recoveryCodes.map((code, index) => (
                    <ListItem
                      key={code}
                      disableGutters
                      divider={index < replacement.recoveryCodes.length - 1}
                    >
                      <ListItemText primary={code} />
                    </ListItem>
                  ))}
                </List>
              </Paper>
            </Stack>
          </Paper>
          {/* A warning, and it was body copy sitting under a green confirmation that said the opposite. It is
              the only Alert on this branch and stays that way: what the server refused is rendered on the form
              branch alone, so this one can never become a second thing shouting at the same time. */}
          <Alert severity="warning">
            {t('mfa.recovery.proveNewFactor')}
          </Alert>
        </>
      ) : (
        <>
          <ProblemMessage
            problem={directFieldProblem ? null : problem}
            claimedFields={recoveryClaimedFields}
            autoFocus={!invalidField}
          />
          <Paper
            variant="outlined"
            component="form"
            sx={section}
            onSubmit={async (event) => {
              event.preventDefault();

              // The proof first, for this action alone: a proof bought to change a password does not pay for
              // replacing a second factor. Nothing is kept between the two calls but what the person typed.
              const proved = await submit(async () => {
                await identity.client.reauthenticate(recoveryAction, password);
                return platform.recoverMfa(recoveryCode);
              });

              setPassword(emptyFieldValue);
              setRecoveryCode(emptyFieldValue);
              if (proved) setReplacement(proved);
            }}
          >
            <Stack spacing={2}>
              {/* Neither field offers autocomplete on purpose: a proof re-typed for this action and a one-time
                  code are exactly the two things a password manager must not be able to fill. */}
              <TextField
                id="mfa-recovery-password"
                inputRef={passwordInput}
                label={t('mfa.recovery.password')}
                type="password"
                required
                fullWidth
                slotProps={requiredField}
                error={invalidPassword}
                helperText={invalidCredentialProof ? t('errors:invalid_credential_proof') : passwordError.helperText}
                value={password}
                onChange={(event) => {
                  setPassword(event.target.value);
                  if (invalidCredentialProof) clearProblem();
                }}
              />
              <TextField
                id="mfa-recovery-code"
                inputRef={recoveryCodeInput}
                label={t('mfa.recovery.code')}
                type="text"
                required
                fullWidth
                slotProps={requiredField}
                error={invalidRecoveryCode}
                helperText={invalidRecoveryCodeDirect ? t('errors:invalid_recovery_code') : recoveryCodeError.helperText}
                value={recoveryCode}
                onChange={(event) => {
                  setRecoveryCode(event.target.value);
                  if (invalidRecoveryCodeDirect) clearProblem();
                }}
              />
              <Button type="submit" variant="contained" disabled={isBusy} sx={leading}>
                {t('mfa.recovery.submit')}
              </Button>
            </Stack>
          </Paper>
        </>
      )}
    </Stack>
  );
}

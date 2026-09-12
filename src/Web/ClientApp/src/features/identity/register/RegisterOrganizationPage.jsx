import { useEffect, useState } from 'react';
import { Link as RouterLink } from 'react-router-dom';
import Alert from '@mui/material/Alert';
import Button from '@mui/material/Button';
import Link from '@mui/material/Link';
import Paper from '@mui/material/Paper';
import Stack from '@mui/material/Stack';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import { useIdentity } from '../context/IdentityProvider';
import {
  claimedFieldNames,
  clearFieldError,
  fieldErrorText,
  organizationRegistrationFields,
  selectFieldErrors,
  validateOrganizationRegistration,
} from '../fieldErrors';
import { ProblemMessage } from '../ProblemMessage';
import { useSubmit } from '../useSubmit';
import { useTranslation } from '../../../i18n';

/** Required without the asterisk MUI would add, which would rename the field for everything that reads its label. */
const requiredField = { inputLabel: { required: false } };

const card = { p: { xs: 3, sm: 4 } };
const fieldIds = {
  legalName: 'register-legal-name',
  cuit: 'register-cuit',
  email: 'register-email',
  password: 'register-password',
};
const signedInRegistrationFields = ['legalName', 'cuit'];
const signInPath = `/login?returnUrl=${encodeURIComponent('/organizations/register')}`;

const focusFirstField = (errors, fields) => {
  const field = fields.find((candidate) => errors[candidate]?.length > 0);
  if (field) document.getElementById(fieldIds[field])?.focus();
};

const fields = {
  legalName: 'legalName',
  cuit: 'cuit',
  email: 'email',
  password: 'password',
};

/**
 * Registration answers with a neutral bodyless 202 whether or not the address is already taken, so the page says
 * the same thing in either case. Telling the visitor which one happened would answer a question the API
 * deliberately refuses to answer (SPEC section 6).
 */
export function RegisterOrganizationPage() {
  const { t } = useTranslation('identity');
  const identity = useIdentity();
  const signedIn = identity.isAuthenticated;
  const activeFields = signedIn ? signedInRegistrationFields : organizationRegistrationFields;
  const [form, setForm] = useState({ email: '', password: '', legalName: '', cuit: '' });
  const [clientFieldErrors, setClientFieldErrors] = useState({});
  const [clearedServerFields, setClearedServerFields] = useState([]);
  const { submit, problem, isBusy, result } = useSubmit((request) => identity.client.registerOrganization(request));
  const fieldErrors = {
    ...selectFieldErrors(
      problem,
      activeFields.filter((field) => !clearedServerFields.includes(field)),
    ),
    ...clientFieldErrors,
  };
  const update = (field) => (event) => {
    setForm((current) => ({ ...current, [field]: event.target.value }));
    setClientFieldErrors((current) => clearFieldError(current, field));
    setClearedServerFields((current) => current.includes(field) ? current : [...current, field]);
  };

  useEffect(() => {
    if (!problem) return;
    const next = selectFieldErrors(problem, activeFields);
    focusFirstField(next, activeFields);
  }, [activeFields, problem]);

  const submitForm = (event) => {
    event.preventDefault();
    const next = validateOrganizationRegistration(form, !signedIn);
    if (Object.keys(next).length > 0) {
      setClientFieldErrors(next);
      setClearedServerFields([]);
      focusFirstField(next, activeFields);
      return;
    }

    setClientFieldErrors({});
    setClearedServerFields([]);
    submit(signedIn ? { ...form, email: '', password: '' } : form);
  };

  return (
    <Paper component="section" elevation={3} aria-labelledby="register-heading" sx={card}>
      <Stack spacing={3}>
        <Typography id="register-heading" component="h1" variant="h5">{t('register.organization.title')}</Typography>
        {/* One frame, two states. The acknowledgement used to be a second copy of this card, which is how the two
            drifted: only the form branch ever rendered a refusal, so a failure that arrived after a success had
            nowhere to go. The heading, its id and the problem slot now belong to the card rather than to a
            branch, and what changes is only what sits under them. */}
        <ProblemMessage problem={problem} claimedFields={claimedFieldNames(problem, activeFields)} />
        {!identity.isLoading && (result ? (
          <Stack spacing={2}>
            <Alert severity="success" role="status">
              If that address can register, we have sent it a confirmation link. Check the inbox.
            </Alert>
            {!signedIn && (
              <Stack spacing={1}>
                <Typography variant="body2">
                  If you already have an account, we emailed you instead with instructions to sign in and add the organization.{' '}
                  <Link component={RouterLink} to={signInPath}>Sign in</Link> now.
                </Typography>
                <Typography variant="body2">
                  Didn&apos;t get an email? Check your spam folder and wait a few minutes.
                </Typography>
              </Stack>
            )}
          </Stack>
        ) : (
        <Stack component="form" spacing={3} noValidate onSubmit={submitForm}>
          {/* Two things are being registered at once — the company, and the person who will sign in for it — so
              the fields are asked for in those two groups rather than as one run of four. */}
          <Stack spacing={2}>
            <TextField
              id="register-legal-name"
              label={t('register.organization.legalName')}
              required
              fullWidth
              slotProps={requiredField}
              value={form.legalName}
              onChange={update('legalName')}
              error={Boolean(fieldErrors.legalName)}
              helperText={fieldErrorText(fieldErrors, 'legalName', t) || undefined}
            />
            <TextField
              id="register-cuit"
              label={t('register.organization.cuit')}
              required
              fullWidth
              slotProps={requiredField}
              value={form.cuit}
              onChange={update('cuit')}
              error={Boolean(fieldErrors.cuit)}
              helperText={fieldErrorText(fieldErrors, 'cuit', t) || undefined}
            />
          </Stack>
          {!signedIn && (
            <Stack spacing={2}>
              <TextField
                id="register-email"
                label="Email"
                type="email"
                autoComplete="username"
                required
                fullWidth
                slotProps={requiredField}
                value={form.email}
                onChange={update('email')}
                error={Boolean(fieldErrors.email)}
                helperText={fieldErrorText(fieldErrors, 'email', t) || undefined}
              />
              <TextField
                id="register-password"
                label="Password"
                type="password"
                autoComplete="new-password"
                required
                fullWidth
                slotProps={requiredField}
                value={form.password}
                onChange={update('password')}
                error={Boolean(fieldErrors.password)}
                helperText={fieldErrorText(fieldErrors, 'password', t) || undefined}
              />
            </Stack>
          )}
          <Button type="submit" variant="contained" size="large" fullWidth disabled={isBusy}>Register</Button>
        </Stack>
        ))}
      </Stack>
    </Paper>
  );
}

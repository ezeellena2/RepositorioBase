import { useEffect, useRef, useState } from 'react';
import { Link as RouterLink } from 'react-router-dom';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Divider from '@mui/material/Divider';
import Link from '@mui/material/Link';
import Paper from '@mui/material/Paper';
import Stack from '@mui/material/Stack';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import { toProblem } from '../api/apiTransport';
import { useIdentity } from '../context/IdentityProvider';
import {
  claimedFieldNames,
  fieldErrorText,
  selectFieldErrors,
} from '../fieldErrors';
import { ProblemMessage } from '../ProblemMessage';
import { useFragmentToken } from '../useFragmentToken';
import { useSubmit } from '../useSubmit';
import { Trans, useTranslation } from '../../../i18n';

/** Required without the asterisk MUI would add, which would rename the field for everything that reads its label. */
const requiredField = { inputLabel: { required: false } };
const resetFields = ['newPassword'];
const resetFieldIds = { newPassword: 'reset-password' };
const changeFields = ['password', 'newPassword'];
const changeFieldIds = { password: 'change-current', newPassword: 'change-next' };

const focusFirstField = (errors, fields, fieldIds) => {
  const field = fields.find((candidate) => errors[candidate]?.length > 0);
  if (field) document.getElementById(fieldIds[field])?.focus();
};

/**
 * Three screens, two frames, because two of these are entrances and one is not.
 *
 * `card` is the public entrance treatment: the raised, generously padded card Layout centres on its own in a
 * `Container maxWidth="xs"` for a visitor who has no session yet and no shell around them. `panel` and `section`
 * are what a screen inside the application gets instead — the page chooses its own width, and the Paper is one
 * section of that page rather than the page itself.
 */
const card = { p: { xs: 3, sm: 4 } };
const panel = { maxWidth: 560 };
const section = { p: { xs: 2, sm: 3 } };
/** Supporting copy hangs off its title rather than standing as a section of its own. */
const supporting = { mt: 0.5 };
const emptyToken = '';

/**
 * "I forgot my password." The answer is the same whatever address is typed, so this page says the same thing
 * whatever happened — telling a visitor whether an address has an account would make this the enumeration route
 * the rest of the system is careful not to be (IA-REQ-029).
 *
 * Having asked is a state of this card, not a second card. What is above the divider changes — the form gives way
 * to what was sent — and what is below it does not: the way back to signing in is the reason somebody is on this
 * screen at all, and it has to survive the ask. Somebody who mistyped the address, or who remembers the password
 * while the mail is still in flight, is otherwise left holding a card with nothing on it but good news.
 */
export function ForgotPasswordPage() {
  const identity = useIdentity();
  const { t } = useTranslation('identity');
  const [email, setEmail] = useState('');
  const { submit, problem, isBusy, result } = useSubmit((address) => identity.client.requestPasswordRecovery(address));

  return (
    <Paper component="section" elevation={3} aria-labelledby="forgot-heading" sx={card}>
      <Stack spacing={3}>
        <Typography id="forgot-heading" component="h1" variant="h5">{t('credentials.forgot.title')}</Typography>

        {result ? (
          <Alert severity="success" role="status">
            {t('credentials.forgot.acknowledgement')}
          </Alert>
        ) : (
          <>
            <ProblemMessage problem={problem} />
            <Stack component="form" spacing={2} onSubmit={(event) => { event.preventDefault(); submit(email); }}>
              <TextField
                id="forgot-email"
                label={t('login.email')}
                type="email"
                autoComplete="username"
                required
                fullWidth
                slotProps={requiredField}
                value={email}
                onChange={(event) => setEmail(event.target.value)}
              />
              {/* Full width and left where it is: on a public entrance card the card *is* the form, and this is
                  the documented exception to the left-aligned submit the rest of the product uses. */}
              <Button type="submit" variant="contained" size="large" fullWidth disabled={isBusy}>{t('credentials.forgot.submit')}</Button>
            </Stack>
          </>
        )}

        <Divider />
        <Typography variant="body2">
          <Trans
            i18nKey="identity:credentials.forgot.remembered"
            components={{ signIn: <Link component={RouterLink} to="/login" /> }}
          />
        </Typography>
      </Stack>
    </Paper>
  );
}

/**
 * The screen the reset mail opens. The token arrives in the fragment and is erased from the address bar before
 * anything else happens, and the reset issues no session: the person signs in afterwards with what they just chose.
 *
 * Which is why the settled state carries a button and not a sentence. Signing in is not a footnote to the reset,
 * it is the rest of the journey and the only thing left to do here — so it takes the weight the submit had before
 * it left the tree, and the card still has exactly one thing on it worth pressing.
 */
export function ResetPasswordPage() {
  const identity = useIdentity();
  const { t } = useTranslation('identity');
  const token = useFragmentToken();
  const passwordInput = useRef(null);
  const [password, setPassword] = useState('');
  const [clearedServerFields, setClearedServerFields] = useState([]);
  const { submit, problem, isBusy, result } = useSubmit((secret, next) => identity.client.resetPassword(secret, next));
  const isSet = result !== null && result !== undefined;
  const fieldErrors = selectFieldErrors(
    problem,
    resetFields.filter((field) => !clearedServerFields.includes(field)),
  );

  useEffect(() => {
    if (!problem) return;
    const nextErrors = selectFieldErrors(problem, resetFields);
    if (nextErrors.newPassword) passwordInput.current?.focus();
  }, [problem]);

  const editPassword = (event) => {
    setPassword(event.target.value);
    setClearedServerFields((current) => current.includes('newPassword') ? current : [...current, 'newPassword']);
  };

  const resetPassword = (event) => {
    event.preventDefault();
    setClearedServerFields([]);
    submit(token ?? '', password);
  };

  return (
    <Paper component="section" elevation={3} aria-labelledby="reset-heading" sx={card}>
      <Stack spacing={3}>
        {/* Why the submit is dead sits with the title rather than under the form: it is the condition the whole
            screen is in, not a footnote to the field. */}
        <Box>
          <Typography id="reset-heading" component="h1" variant="h5">{t('credentials.reset.title')}</Typography>
          {!token && (
            <Typography variant="body2" color="text.secondary" sx={supporting}>
              {t('credentials.reset.missingToken')}
            </Typography>
          )}
        </Box>

        {isSet ? (
          <>
            <Alert severity="success" role="status">{t('credentials.reset.success')}</Alert>
            <Button component={RouterLink} to="/login" variant="contained" size="large" fullWidth>{t('login.submit')}</Button>
          </>
        ) : (
          <>
            <ProblemMessage
              problem={problem}
              claimedFields={claimedFieldNames(problem, resetFields)}
              fieldIds={resetFieldIds}
            />
            <Stack
              component="form"
              spacing={2}
              onSubmit={resetPassword}
            >
              <TextField
                id="reset-password"
                inputRef={passwordInput}
                label="New password"
                type="password"
                autoComplete="new-password"
                required
                fullWidth
                slotProps={requiredField}
                error={Boolean(fieldErrors.newPassword)}
                helperText={fieldErrorText(fieldErrors, 'newPassword', t) || undefined}
                value={password}
                onChange={editPassword}
              />
              <Button type="submit" variant="contained" size="large" fullWidth disabled={isBusy || !token}>
                {t('credentials.reset.submit')}
              </Button>
            </Stack>
          </>
        )}
      </Stack>
    </Paper>
  );
}

/**
 * Changing a password from inside the account. It asks for the current one to buy a server-side proof, then sends
 * the change — the API never receives a `currentPassword` field, and this page keeps neither value.
 *
 * This is the one of the three that a session already opened, so it is composed as a page and not as a card: the
 * title stands on the page, what the server said stands under it, and the two fields are one outlined section of
 * that page. The raised card belongs to the entrance, where there is nothing around it to belong to.
 */
export function ChangePasswordPage() {
  const identity = useIdentity();
  const { t } = useTranslation('identity');
  const [current, setCurrent] = useState('');
  const [next, setNext] = useState('');
  const [problem, setProblem] = useState(null);
  const [isBusy, setIsBusy] = useState(false);
  const [done, setDone] = useState(false);
  const [clearedServerFields, setClearedServerFields] = useState([]);
  const fieldErrors = selectFieldErrors(
    problem,
    changeFields.filter((field) => !clearedServerFields.includes(field)),
  );

  useEffect(() => {
    if (!problem) return;
    focusFirstField(selectFieldErrors(problem, changeFields), changeFields, changeFieldIds);
  }, [problem]);

  const change = async () => {
    setIsBusy(true);
    setProblem(null);
    setClearedServerFields([]);
    try {
      await identity.client.reauthenticate('credentials.password.change', current);
      await identity.client.changePassword(next);
      setCurrent('');
      setNext('');
      setDone(true);
    } catch (error) {
      setProblem(toProblem(error));
    } finally {
      setIsBusy(false);
    }
  };

  /**
   * A confirmation reports a change that was made, so it stops reporting anything the moment the fields it speaks
   * for are being filled in again. Clearing it on the first keystroke is what keeps a settled message from standing
   * over a live form and claiming a second change already happened.
   */
  const edit = (field, set) => (event) => {
    setDone(false);
    set(event.target.value);
    setClearedServerFields((current) => current.includes(field) ? current : [...current, field]);
  };

  return (
    <Stack component="section" aria-labelledby="change-heading" spacing={3} sx={panel}>
      <Box>
        <Typography id="change-heading" component="h1" variant="h5">{t('credentials.change.title')}</Typography>
      </Box>

      <ProblemMessage
        problem={problem}
        claimedFields={claimedFieldNames(problem, changeFields)}
        fieldIds={changeFieldIds}
      />

      {/* Deliberately role="status" and not the role="alert" MUI gives every severity: a refusal can arrive over a
          confirmation, and a page that resolves its alert as one element cannot be handed two. */}
      {done && (
        <Alert severity="success" role="status">
          {t('credentials.change.success')}
        </Alert>
      )}

      <Paper
        variant="outlined"
        component="form"
        sx={section}
        onSubmit={(event) => { event.preventDefault(); change(); }}
      >
        <Stack spacing={2}>
          <TextField
            id="change-current"
            label={t('credentials.change.currentPassword')}
            type="password"
            autoComplete="current-password"
            required
            fullWidth
            slotProps={requiredField}
            error={Boolean(fieldErrors.password)}
            helperText={fieldErrorText(fieldErrors, 'password', t) || undefined}
            value={current}
            onChange={edit('password', setCurrent)}
          />
          <TextField
            id="change-next"
            label={t('credentials.newPassword')}
            type="password"
            autoComplete="new-password"
            required
            fullWidth
            slotProps={requiredField}
            error={Boolean(fieldErrors.newPassword)}
            helperText={fieldErrorText(fieldErrors, 'newPassword', t) || undefined}
            value={next}
            onChange={edit('newPassword', setNext)}
          />
          <Button type="submit" variant="contained" disabled={isBusy} sx={{ alignSelf: 'flex-start' }}>
            {t('credentials.change.submit')}
          </Button>
        </Stack>
      </Paper>
    </Stack>
  );
}

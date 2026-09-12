import { useCallback, useEffect, useState } from 'react';
import { Link as RouterLink } from 'react-router-dom';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import FormControl from '@mui/material/FormControl';
import InputLabel from '@mui/material/InputLabel';
import Link from '@mui/material/Link';
import NativeSelect from '@mui/material/NativeSelect';
import Paper from '@mui/material/Paper';
import Skeleton from '@mui/material/Skeleton';
import Stack from '@mui/material/Stack';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import { toProblem } from '../api/apiTransport';
import { useIdentity } from '../context/IdentityProvider';
import {
  claimedFieldNames,
  clearFieldError,
  fieldErrorText,
  personalRegistrationFields,
  selectFieldErrors,
  validatePersonalRegistration,
} from '../fieldErrors';
import { ProblemMessage } from '../ProblemMessage';
import { useSubmit } from '../useSubmit';
import { Trans, useTranslation } from '../../../i18n';

/** Required without the asterisk MUI would add, which would rename the field for everything that reads its label. */
const requiredField = { inputLabel: { required: false } };

const card = { p: { xs: 3, sm: 4 } };
const page = { maxWidth: 560 };
const section = { p: { xs: 2, sm: 3 } };
const header = { alignItems: 'flex-start', justifyContent: 'space-between', flexWrap: 'wrap' };
/**
 * Label beside value where there is room for both, and label above value where there is not. Forced to two tracks
 * at every width, the `auto` column takes whatever "DNI" or a country and type need and the value column keeps an
 * unbreakable address in what is left — which on a phone is narrower than the address, so the card overflows.
 */
const facts = {
  m: 0,
  display: 'grid',
  gridTemplateColumns: { xs: '1fr', sm: 'auto 1fr' },
  columnGap: 2,
  rowGap: { xs: 1, sm: 2 },
};
const fact = { m: 0 };
const supporting = { mt: 0.5, maxWidth: 640 };
const startOfRow = { alignSelf: 'flex-start' };
const personalField = {
  fullName: 'fullName',
  displayName: 'displayName',
  documentNumber: 'documentNumber',
  email: 'email',
  password: 'password',
};
const DocumentType = 'DNI';
const organizationRegisterPath = '/organizations/register';
const disputeReasonId = 'dispute-reason';
const numericDocumentField = { ...requiredField, htmlInput: { inputMode: 'numeric', autoComplete: 'off' } };
const disputeReason = {
  typedWrongAtSignup: 'TypedWrongAtSignup',
  documentReissued: 'DocumentReissued',
  recordedByMistake: 'RecordedByMistake',
};
const recordedDocumentStatus = 'recorded';
const personalRegistrationFieldIds = {
  fullName: 'personal-full-name',
  displayName: 'personal-display-name',
  documentNumber: 'personal-document',
  email: 'personal-email',
  password: 'personal-password',
};
const addPersonalContextFields = ['fullName', 'displayName', 'documentNumber'];
const personalProfileFields = ['fullName', 'displayName'];

const focusFirstPersonalRegistrationField = (errors) => {
  const field = personalRegistrationFields.find((candidate) => errors[candidate]?.length > 0);
  if (field) document.getElementById(personalRegistrationFieldIds[field])?.focus();
};

/**
 * A newcomer setting up their own account. Like the organization signup it answers with a neutral bodyless 202
 * whether or not the address is already taken, so this page says the same thing in either case (SPEC section 6).
 */
export function PersonalRegisterPage() {
  const identity = useIdentity();
  const { t } = useTranslation();
  const [form, setForm] = useState({ email: '', password: '', fullName: '', displayName: '', documentNumber: '' });
  const [clientFieldErrors, setClientFieldErrors] = useState({});
  const [clearedServerFields, setClearedServerFields] = useState([]);
  const { submit, problem, isBusy, result } = useSubmit((request) => identity.client.registerPersonal(request));
  const fieldErrors = {
    ...selectFieldErrors(
      problem,
      personalRegistrationFields.filter((field) => !clearedServerFields.includes(field)),
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
    const next = selectFieldErrors(problem, personalRegistrationFields);
    focusFirstPersonalRegistrationField(next);
  }, [problem]);

  const submitForm = (event) => {
    event.preventDefault();
    const next = validatePersonalRegistration(form);
    if (Object.keys(next).length > 0) {
      setClientFieldErrors(next);
      setClearedServerFields([]);
      focusFirstPersonalRegistrationField(next);
      return;
    }

    setClientFieldErrors({});
    setClearedServerFields([]);
    submit(form);
  };

  if (result) {
    return (
      <Paper component="section" elevation={3} aria-labelledby="personal-register-heading" sx={card}>
        <Stack spacing={3}>
          <Typography id="personal-register-heading" component="h1" variant="h5">{t('identity:people.register.title')}</Typography>
          <Alert severity="success" role="status">
            {t('identity:register.organization.acknowledgement')}
          </Alert>
        </Stack>
      </Paper>
    );
  }

  return (
    <Paper component="section" elevation={3} aria-labelledby="personal-register-heading" sx={card}>
      <Stack spacing={3}>
        <Typography id="personal-register-heading" component="h1" variant="h5">{t('identity:people.register.title')}</Typography>
        <ProblemMessage problem={problem} claimedFields={claimedFieldNames(problem, personalRegistrationFields)} />
        <Stack component="form" spacing={3} noValidate onSubmit={submitForm}>
          {/* Two things are asked for at once — who this person is, and the credential they will sign in with — so
              the five fields are asked in those two groups rather than as one undifferentiated run, the same way
              the organization signup asks for the company and then for the person who will sign in for it. */}
          <Stack spacing={2}>
            <TextField
              id="personal-full-name"
              label={t('identity:people.fullName')}
              required
              fullWidth
              slotProps={requiredField}
              value={form.fullName}
              onChange={update(personalField.fullName)}
              error={Boolean(fieldErrors.fullName)}
              helperText={fieldErrorText(fieldErrors, personalField.fullName, t) || undefined}
            />
            <TextField
              id="personal-display-name"
              label={t('identity:people.displayName')}
              required
              fullWidth
              slotProps={requiredField}
              value={form.displayName}
              onChange={update(personalField.displayName)}
              error={Boolean(fieldErrors.displayName)}
              helperText={fieldErrorText(fieldErrors, personalField.displayName, t) || undefined}
            />
            <TextField
              id="personal-document"
              label={DocumentType}
              required
              fullWidth
              slotProps={numericDocumentField}
              value={form.documentNumber}
              onChange={update(personalField.documentNumber)}
              error={Boolean(fieldErrors.documentNumber)}
              helperText={fieldErrorText(fieldErrors, personalField.documentNumber, t) || undefined}
            />
          </Stack>
          <Stack spacing={2}>
            <TextField
              id="personal-email"
              label={t('identity:login.email')}
              type="email"
              autoComplete="username"
              required
              fullWidth
              slotProps={requiredField}
              value={form.email}
              onChange={update(personalField.email)}
              error={Boolean(fieldErrors.email)}
              helperText={fieldErrorText(fieldErrors, personalField.email, t) || undefined}
            />
            <TextField
              id="personal-password"
              label={t('identity:login.password')}
              type="password"
              autoComplete="new-password"
              required
              fullWidth
              slotProps={requiredField}
              value={form.password}
              onChange={update(personalField.password)}
              error={Boolean(fieldErrors.password)}
              helperText={fieldErrorText(fieldErrors, personalField.password, t) || undefined}
            />
          </Stack>
          <Button type="submit" variant="contained" size="large" fullWidth disabled={isBusy}>{t('common:navigation.register')}</Button>
        </Stack>
        <Typography variant="body2">
          <Trans
            i18nKey="identity:people.register.organizationInstead"
            components={{ organization: <Link component={RouterLink} to={organizationRegisterPath} /> }}
          />
        </Typography>
      </Stack>
    </Paper>
  );
}

/**
 * Saying the recorded document is wrong. The screen offers this unconditionally when a document is recorded,
 * because whether a correction is *possible* must not depend on what the person can see about anybody else —
 * only on whether they already have a dispute open (IA-REQ-058).
 *
 * The claimed number is typed here and sent once. Nothing keeps it: no state survives the submit, and the answer
 * carries only an opaque identifier back.
 */
function DocumentDispute({ client, available, country, type }) {
  const { t } = useTranslation();
  const [claimedNumber, setClaimedNumber] = useState('');
  const [reasonCode, setReasonCode] = useState(disputeReason.typedWrongAtSignup);
  const { submit, problem, isBusy, result } = useSubmit((request) => client.openDocumentDispute(request));

  // Both endings are states of this block, not replacements for it: the card and its heading stay, and what
  // changes is the outcome inside them. A bare Alert here would drop the section's frame and its name, leaving a
  // floating strip between two Papers at the moment the page most needs to say which part of it answered.
  if (!available || result) {
    return (
      <Paper variant="outlined" component="section" aria-labelledby="dispute-heading" sx={section}>
        <Stack spacing={2}>
          <Typography id="dispute-heading" component="h3" variant="subtitle1">{t('identity:people.dispute.title')}</Typography>
          {result ? (
            <Alert severity="success" role="status">
              {t('identity:people.dispute.success')}
            </Alert>
          ) : (
            <Alert severity="info">{t('identity:people.dispute.alreadyOpen')}</Alert>
          )}
        </Stack>
      </Paper>
    );
  }

  return (
    <Paper
      variant="outlined"
      component="form"
      aria-labelledby="dispute-heading"
      sx={section}
      onSubmit={(event) => {
        event.preventDefault();
        submit({ claimedCountry: country, claimedType: type, claimedNumber, reasonCode });
        setClaimedNumber('');
      }}
    >
      <Stack spacing={2}>
        <Box>
          <Typography id="dispute-heading" component="h3" variant="subtitle1">{t('identity:people.dispute.title')}</Typography>
          <Typography variant="body2" color="text.secondary" sx={supporting}>
            {t('identity:people.dispute.description')}
          </Typography>
        </Box>
        <ProblemMessage problem={problem} />
        <TextField
          id="dispute-number"
          label={t('identity:people.dispute.number')}
          required
          fullWidth
          slotProps={requiredField}
          value={claimedNumber}
          onChange={(event) => setClaimedNumber(event.target.value)}
        />
        <FormControl fullWidth>
          <InputLabel htmlFor={disputeReasonId}>{t('identity:people.dispute.reason')}</InputLabel>
          <NativeSelect
            inputProps={{ id: disputeReasonId, name: disputeReasonId }}
            value={reasonCode}
            onChange={(event) => setReasonCode(event.target.value)}
          >
            <option value={disputeReason.typedWrongAtSignup}>{t('identity:people.dispute.typedWrongAtSignup')}</option>
            <option value={disputeReason.documentReissued}>{t('identity:people.dispute.documentReissued')}</option>
            <option value={disputeReason.recordedByMistake}>{t('identity:people.dispute.recordedByMistake')}</option>
          </NativeSelect>
        </FormControl>
        {/* Outlined, not filled: the page's own action is saving the names below, and a correction that takes two
            parties and days of review is not the thing to press by reflex. */}
        <Button type="submit" variant="outlined" disabled={isBusy} sx={startOfRow}>
          {t('identity:people.dispute.submit')}
        </Button>
      </Stack>
    </Paper>
  );
}

/**
 * Adding a personal context to an identity that already exists.
 *
 * It is deliberately not the signup at `/personal/register`. That page is for a stranger: it asks for an address
 * and a password this person already has, and the route behind it answers the same neutral acknowledgement
 * whether or not the address is taken — so somebody who is already signed in would be told nothing and given
 * nothing. What they need is the claim itself, which is the identity they are holding plus a document.
 */
function AddPersonalContext({ client, notice, onAdded }) {
  const { t } = useTranslation();
  const [form, setForm] = useState({ fullName: '', displayName: '', documentNumber: '' });
  const [created, setCreated] = useState(false);
  const [clearedServerFields, setClearedServerFields] = useState([]);
  const { submit, problem, isBusy } = useSubmit(async (request) => {
    await client.createPersonalContext(request);
    setCreated(true);
    setForm({ fullName: '', displayName: '', documentNumber: '' });
    await onAdded();
  });
  const fieldErrors = selectFieldErrors(
    problem,
    addPersonalContextFields.filter((field) => !clearedServerFields.includes(field)),
  );
  const update = (field) => (event) => {
    setForm((current) => ({ ...current, [field]: event.target.value }));
    setClearedServerFields((current) => current.includes(field) ? current : [...current, field]);
  };

  useEffect(() => {
    const next = selectFieldErrors(problem, addPersonalContextFields);
    for (const [field, id] of [
      ['fullName', 'add-personal-full-name'],
      ['displayName', 'add-personal-display-name'],
      ['documentNumber', 'add-personal-document'],
    ]) {
      if (next[field]) {
        document.getElementById(id)?.focus();
        break;
      }
    }
  }, [problem]);

  // Saving succeeded before either read begins. A failed refresh must never offer to create it again — and the
  // card stays where it was while the refresh runs, so the page does not collapse to a strip at the one moment
  // the person is waiting on it.
  if (created) {
    return (
      <Paper variant="outlined" sx={section}>
        <Alert severity="success" role="status">{t('identity:people.context.success')}</Alert>
      </Paper>
    );
  }

  return (
    <Paper
      variant="outlined"
      component="form"
      sx={section}
      onSubmit={(event) => { event.preventDefault(); setClearedServerFields([]); submit(form); }}
    >
      <Stack spacing={2}>
        {/* The page's own explanation of why this form is the only thing on it. It belongs inside the card rather
            than floating above it: one block that says what is missing and offers the thing that supplies it. */}
        {notice}
        <ProblemMessage problem={problem} claimedFields={claimedFieldNames(problem, addPersonalContextFields)} />
        <TextField
          id="add-personal-full-name"
          label={t('identity:people.fullName')}
          required
          fullWidth
          slotProps={requiredField}
          value={form.fullName}
          onChange={update(personalField.fullName)}
          error={Boolean(fieldErrors.fullName)}
          helperText={fieldErrorText(fieldErrors, personalField.fullName, t) || undefined}
        />
        <TextField
          id="add-personal-display-name"
          label={t('identity:people.displayName')}
          required
          fullWidth
          slotProps={requiredField}
          value={form.displayName}
          onChange={update(personalField.displayName)}
          error={Boolean(fieldErrors.displayName)}
          helperText={fieldErrorText(fieldErrors, personalField.displayName, t) || undefined}
        />
        <TextField
          id="add-personal-document"
          label={DocumentType}
          required
          fullWidth
          slotProps={numericDocumentField}
          value={form.documentNumber}
          onChange={update(personalField.documentNumber)}
          error={Boolean(fieldErrors.documentNumber)}
          helperText={fieldErrorText(fieldErrors, personalField.documentNumber, t) || undefined}
        />
        <Button type="submit" variant="contained" disabled={isBusy} sx={startOfRow}>
          {t('identity:people.context.submit')}
        </Button>
      </Stack>
    </Paper>
  );
}

/**
 * The header every branch of the profile opens with, so the title is in the same place whether the page is
 * waiting, refusing, empty or loaded.
 *
 * The slot beside the title is where a screen's primary action goes, and here it stays empty on purpose. This
 * screen's only primary action is saving the two names, and a save belongs under the fields it saves rather than
 * at the far end of a header row from them; nothing else on the page is an action worth inventing to fill it.
 */
const ProfileHeader = () => {
  const { t } = useTranslation();

  return (
    <Stack direction="row" spacing={2} sx={header}>
      <Box>
        <Typography id="profile-heading" component="h1" variant="h5">{t('common:navigation.yourProfile')}</Typography>
      </Box>
    </Stack>
  );
};

/**
 * The owner's own profile. The document is shown masked and is never editable here: correcting one is a separate
 * verified process that takes two parties, and all this screen can do is start it. `correctionAvailable` is what
 * decides whether the form is offered, because at most one dispute is open at a time (IA-REQ-058).
 */
export function PersonalProfilePage() {
  const identity = useIdentity();
  const { t } = useTranslation();
  const [loaded, setLoaded] = useState(null);
  const [loadProblem, setLoadProblem] = useState(null);
  const [edits, setEdits] = useState(null);
  const [clearedServerFields, setClearedServerFields] = useState([]);
  const { submit, problem, isBusy, result } = useSubmit(async (request) => {
    const saved = await identity.client.updatePersonalProfile(request);
    setEdits((current) => {
      const latest = current ?? { fullName: request.fullName, displayName: request.displayName };
      const fullNameChanged = latest.fullName !== request.fullName;
      const displayNameChanged = latest.displayName !== request.displayName;
      if (!fullNameChanged && !displayNameChanged) return null;

      return {
        fullName: fullNameChanged ? latest.fullName : saved.fullName,
        displayName: displayNameChanged ? latest.displayName : saved.displayName,
      };
    });
    setClearedServerFields([]);
    return saved;
  });
  const fieldErrors = selectFieldErrors(
    problem,
    personalProfileFields.filter((field) => !clearedServerFields.includes(field)),
  );

  // The saved response is the newest truth about the row, so it wins over what was loaded rather than being
  // copied into state after the fact. Deriving it keeps one source and avoids a render that syncs itself.
  const profile = result ?? loaded;
  const form = edits ?? (profile ? { fullName: profile.fullName, displayName: profile.displayName } : { fullName: '', displayName: '' });

  const load = useCallback(async () => {
    try {
      setLoaded(await identity.client.getPersonalProfile());
      setLoadProblem(null);
    } catch (error) {
      setLoaded(null);
      setLoadProblem(toProblem(error));
    }
  }, [identity.client]);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      if (!cancelled) await load();
    })();
    return () => { cancelled = true; };
  }, [load]);

  const update = (field) => (event) => {
    const { value } = event.target;
    setEdits((current) => ({ ...(current ?? { fullName: profile?.fullName ?? '', displayName: profile?.displayName ?? '' }), [field]: value }));
    setClearedServerFields((current) => current.includes(field) ? current : [...current, field]);
  };

  useEffect(() => {
    const next = selectFieldErrors(problem, personalProfileFields);
    if (next.fullName) document.getElementById('profile-full-name')?.focus();
    else if (next.displayName) document.getElementById('profile-display-name')?.focus();
  }, [problem]);

  if (loadProblem?.code === 'personal_profile_not_found') {
    return (
      <Stack component="section" aria-labelledby="profile-heading" spacing={3} sx={page}>
        <ProfileHeader />
        {/* One empty state, not two blocks that happen to be adjacent: the sentence saying there is nothing here
            yet and the form that creates it are the same thing, so the sentence is handed to the card. */}
        <AddPersonalContext
          client={identity.client}
          notice={<Alert severity="info" role="status">{t('identity:people.context.empty')}</Alert>}
          onAdded={async () => {
            // Failure clears shared context and the protected route shows the existing sign-in/read error.
            if (await identity.reload()) await load();
          }}
        />
      </Stack>
    );
  }

  if (!profile) {
    return (
      <Stack
        component="section"
        aria-labelledby="profile-heading"
        aria-busy={loadProblem === null}
        spacing={3}
        sx={page}
      >
        <ProfileHeader />
        <ProblemMessage problem={loadProblem} />
        {/* The wait holds the shape of the two cards that always come — the facts, then the form for the names —
            so the profile does not arrive by pushing the page down. A read that failed shows the refusal instead,
            and nothing is pretended to be on its way.

            The wait is announced on the section rather than by a `role="status"` region around the skeletons,
            which is what the composition rules otherwise ask for. This route already spends its one status region
            on "You have no personal context yet.", and both the acceptance step and three RTL queries resolve
            that role as a single element — a second one here wins the race, because it is on screen from the
            first paint while the answer that decides which branch renders is still in flight. `aria-busy` on the
            region the heading already names says the same thing without taking that name. */}
        {loadProblem === null && (
          <Stack spacing={3}>
            <Skeleton variant="rounded" height={96} />
            <Skeleton variant="rounded" height={228} />
          </Stack>
        )}
      </Stack>
    );
  }

  return (
    <Stack component="section" aria-labelledby="profile-heading" spacing={3} sx={page}>
      <ProfileHeader />

      {/* What the account is, as the server holds it: neither line is editable here, and the document never
          arrives in full. One list, because the masked number is read back out of exactly this one — and a
          definition list rather than a heading is also what tells this card apart from the two below it, which
          are a correction and a form. None of the three may be given a heading it does not already have. */}
      <Paper variant="outlined" sx={section}>
        <Box component="dl" sx={facts}>
          <Typography component="dt" variant="body2" color="text.secondary">{t('identity:login.email')}</Typography>
          <Typography component="dd" variant="body2" sx={fact}>{profile.email}</Typography>
          {profile.document && (
            <>
              <Typography component="dt" variant="body2" color="text.secondary">
                {profile.document.country} {profile.document.type}
              </Typography>
              <Typography component="dd" variant="body2" sx={fact}>{profile.document.maskedNumber}</Typography>
            </>
          )}
        </Box>
      </Paper>

      {profile.document && profile.document.status === recordedDocumentStatus && (
        <DocumentDispute
          client={identity.client}
          available={profile.document.correctionAvailable}
          country={profile.document.country}
          type={profile.document.type}
        />
      )}

      <Paper
        variant="outlined"
        component="form"
        sx={section}
        onSubmit={(event) => {
          event.preventDefault();
          const submitted = { fullName: form.fullName, displayName: form.displayName };
          setClearedServerFields([]);
          submit({ ...submitted, version: profile.version });
        }}
      >
        <Stack spacing={2}>
          <ProblemMessage problem={problem} claimedFields={claimedFieldNames(problem, personalProfileFields)} />
          <TextField
            id="profile-full-name"
            label={t('identity:people.fullName')}
            required
            fullWidth
            slotProps={requiredField}
            value={form.fullName}
            onChange={update(personalField.fullName)}
            error={Boolean(fieldErrors.fullName)}
            helperText={fieldErrorText(fieldErrors, personalField.fullName, t) || undefined}
          />
          <TextField
            id="profile-display-name"
            label={t('identity:people.displayName')}
            required
            fullWidth
            slotProps={requiredField}
            value={form.displayName}
            onChange={update(personalField.displayName)}
            error={Boolean(fieldErrors.displayName)}
            helperText={fieldErrorText(fieldErrors, personalField.displayName, t) || undefined}
          />
          <Button type="submit" variant="contained" disabled={isBusy} sx={startOfRow}>{t('identity:people.profile.save')}</Button>
        </Stack>
      </Paper>
    </Stack>
  );
}

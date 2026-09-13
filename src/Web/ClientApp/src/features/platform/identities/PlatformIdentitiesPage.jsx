/* eslint-disable i18next/no-literal-string -- bounded wire field name, not display copy. */
import { useCallback, useState } from 'react';
import { visuallyHidden } from '@mui/utils';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Checkbox from '@mui/material/Checkbox';
import Chip from '@mui/material/Chip';
import Divider from '@mui/material/Divider';
import FormControl from '@mui/material/FormControl';
import FormControlLabel from '@mui/material/FormControlLabel';
import InputLabel from '@mui/material/InputLabel';
import LinearProgress from '@mui/material/LinearProgress';
import MenuItem from '@mui/material/MenuItem';
import Select from '@mui/material/Select';
import Paper from '@mui/material/Paper';
import Skeleton from '@mui/material/Skeleton';
import Stack from '@mui/material/Stack';
import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableContainer from '@mui/material/TableContainer';
import TableHead from '@mui/material/TableHead';
import TableRow from '@mui/material/TableRow';
import Typography from '@mui/material/Typography';
import { useIdentity } from '../../identity/context/IdentityProvider';
import { ProblemMessage } from '../../identity/ProblemMessage';
import { PermissionLabel } from '../../identity/PermissionLabel';
import { useSubmit } from '../../identity/useSubmit';
import { useRead } from '../../identity/useRead';
import { usePlatformClient } from '../invitations/PlatformInvitationPages';
import { PlatformStepUpForm } from '../shared/PlatformStepUpForm';
import { usePlatformStepUp } from '../shared/usePlatformStepUp';
import { useTranslation } from '../../../i18n';

/**
 * The closed set the API accepts for an account suspension. It is declared here rather than shared with the
 * panel's tenant reasons: the two sets happen to hold the same four names today, and one changing is not the
 * other changing. A shared constant would make that coincidence into a coupling.
 */
const SUSPENSION_REASONS = ['PolicyViolation', 'SecurityIncident', 'BillingHold', 'OperatorRequest'];
const actionKinds = { suspend: 'suspend', reactivate: 'reactivate' };
const identitiesHeadingId = 'platform-identities-heading';
const identitiesStepUpInputId = 'platform-identities-step-up';
const suspensionReasonInputId = 'platform-identity-suspension-reason';
const suspensionReasonLabelId = 'platform-identity-suspension-reason-label';
const outlinedSubmitVariant = 'outlined';
const readPermission = 'platform.identities.read';

const frame = { maxWidth: 560 };
const header = { alignItems: 'flex-start', justifyContent: 'space-between', flexWrap: 'wrap' };
const section = { p: { xs: 2, sm: 3 } };
const confirmation = { ...section, maxWidth: 560 };
const emptyBlock = { p: 4, textAlign: 'center' };
const rowActions = { justifyContent: { xs: 'flex-start', sm: 'flex-end' }, flexWrap: 'wrap', minWidth: 0 };
const buttons = { flexWrap: 'wrap', alignItems: 'center' };
const selfStart = { alignSelf: 'flex-start' };
const pagerSlot = { px: 2, py: 1.5 };

/**
 * Text- and chip-only rows keep the 44px minimum. A row with an action grows to hold the theme's 40px control,
 * the 6px cell padding above and below it, and the 1px divider. The wait reserves that action-bearing shape so
 * the directory arrives into space already held for it.
 */
const ROW_MIN_HEIGHT = 44;
const ACTION_ROW_HEIGHT = 6 + 40 + 6 + 1;

const mobileValue = { minWidth: 0, overflowWrap: 'anywhere' };
const responsiveTable = (minimumWidth) => (theme) => ({
  minWidth: 0,
  '& .MuiTableRow-root': { height: ROW_MIN_HEIGHT },
  [theme.breakpoints.up('sm')]: { minWidth: minimumWidth },
  [theme.breakpoints.down('sm')]: {
    display: 'block',
    '& .MuiTableHead-root, & .MuiTableHead-root .MuiTableRow-root': {
      display: 'block',
      height: 0,
    },
    '& .MuiTableHead-root .MuiTableCell-root': { ...visuallyHidden },
    '& .MuiTableBody-root': { display: 'block', width: '100%' },
    '& .MuiTableBody-root .MuiTableRow-root': {
      display: 'block',
      width: '100%',
      height: 'auto',
      py: 1,
      borderBottom: 1,
      borderColor: 'divider',
    },
    '& .MuiTableBody-root .MuiTableRow-root:last-of-type': { borderBottom: 0 },
    '& .MuiTableBody-root .MuiTableCell-root': {
      display: 'grid',
      gridTemplateColumns: 'minmax(112px, 38%) minmax(0, 1fr)',
      gap: 1,
      alignItems: 'center',
      minWidth: 0,
      px: 2,
      py: 0.75,
      borderBottom: 0,
      textAlign: 'left',
      overflowWrap: 'anywhere',
    },
    '& .MuiTableBody-root .MuiTableCell-root[data-mobile-label]::before': {
      content: 'attr(data-mobile-label)',
      ...theme.typography.caption,
      color: 'text.secondary',
      fontWeight: 600,
    },
    '& .MuiTableBody-root .MuiButton-root': {
      maxWidth: '100%',
      whiteSpace: 'normal',
      overflowWrap: 'anywhere',
      textAlign: 'left',
    },
    '& .MuiTableBody-root .MuiChip-root': {
      minWidth: 0,
      maxWidth: '100%',
      height: 'auto',
      minHeight: 24,
      justifySelf: 'start',
    },
    '& .MuiTableBody-root .MuiChip-label': {
      whiteSpace: 'normal',
      overflowWrap: 'anywhere',
      py: 0.25,
    },
  },
});

/**
 * `LinearProgress` is 4px tall and so is the slot that holds it. A reload with rows already on screen is announced
 * inside space the section already occupies: injecting a line instead would move the directory down by a line
 * every time it is re-read, under the hands of somebody reading it.
 */
const progressSlot = { height: 4 };

/**
 * An account state is a closed set the server owns, so the colour is a lookup rather than a condition. A state
 * this screen has not been taught falls back to the neutral chip: it is not an error, it is a state this screen
 * has not been taught. The server code determines both the semantic color and its catalog label; translation
 * changes the presentation without changing which transitions the account permits.
 */
const statusColor = {
  Active: 'success',
  AdministrativelySuspended: 'warning',
  SelfDeactivated: 'warning',
  Closed: 'error',
};

/**
 * Which transition the server would accept from a given state, and therefore the only one worth offering. A
 * reactivation is accepted from `AdministrativelySuspended` and nowhere else; a suspension is accepted from
 * everywhere except that state and `Closed`, which is terminal. Rendering the other combinations would be
 * rendering a button whose only possible answer is a refusal.
 */
const transitionFor = (accountStatus) => {
  if (accountStatus === 'AdministrativelySuspended') return 'reactivate';
  if (accountStatus === 'Closed') return null;
  return 'suspend';
};

/**
 * The same question asked of the caller rather than of the row. Holding `platform.identities.read` without
 * `platform.identities.manage` is a real combination, and offering it a button whose only possible answer is
 * `permission_denied` is the same lie as offering a transition the state does not accept.
 */
const offeredTransition = (accountStatus, mayManage) => (mayManage ? transitionFor(accountStatus) : null);

/**
 * The operator directory of accounts, and the two lifecycle changes it offers (IA-REQ-054).
 *
 * Two different gates answer with `401 recent_mfa_required` and there is nothing on the problem document that
 * tells them apart. Reading the directory needs a session that proved its second factor at all; changing one
 * account needs a *recent* proof. The discriminator is `requiresTwoFactor` on the identity context: while it is
 * true this screen asks for nothing and renders the ceremony instead of the data, and once it is false a refusal
 * of that code can only have come from the recency rule — with the rows already on screen and worth keeping.
 *
 * What the gate must never do is finish the change it interrupted. `onProved` reloads the directory and closes
 * over no action, and the pending confirmation is taken down before the gate goes up, so after a step-up there is
 * nothing armed to fire: asking again is the operator's to do, deliberately.
 */
export function PlatformIdentitiesPage() {
  const { t } = useTranslation('platform');
  const identity = useIdentity();
  const platform = usePlatformClient();
  const [pending, setPending] = useState(null);
  const [reason, setReason] = useState(SUSPENSION_REASONS[0]);
  const [acknowledged, setAcknowledged] = useState(false);
  const [proofRefusal, setProofRefusal] = useState(null);

  const context = identity?.context;
  const permissions = context?.permissions ?? [];
  // For the operator's benefit rather than as a control — the API reauthorizes every call — but a screen that
  // renders a directory its caller may not read produces a refusal where an answer was expected.
  const mayRead = Boolean(identity?.isAuthenticated)
    && context?.activeTenant?.type === 'Platform'
    && permissions.includes(readPermission);
  const mayManage = mayRead && permissions.includes('platform.identities.manage');
  const owesFactor = context?.session?.requiresTwoFactor === true;
  const mayLoad = mayRead && !owesFactor;

  const { data: page, problem: readProblem, refresh, status } = useRead(
    useCallback((options) => platform.listIdentities(options), [platform]),
    mayLoad,
  );
  const { submit, problem: actionProblem, isBusy } = useSubmit(async (action) => action());

  // Reload the rows, and nothing else. It holds no reference to whatever was refused, which is what makes "the
  // gate never replays the change" structural instead of a rule somebody has to keep remembering. The entry gate
  // needs no refresh from here: its read starts itself the moment the factor stops being owed, and asking again
  // would be a second request for the same page.
  const onProved = useCallback(async () => {
    setProofRefusal(null);
    if (mayLoad) await refresh(undefined);
  }, [mayLoad, refresh]);
  const stepUp = usePlatformStepUp(onProved);
  const stepUpProblemForSummary = stepUp.problem?.code === 'invalid_mfa_code' ? null : stepUp.problem;

  const run = (action) => submit(async () => {
    setProofRefusal(null);
    try {
      const outcome = await action();
      setPending(null);
      await refresh(undefined);
      return outcome;
    } catch (failure) {
      const code = failure?.problem?.code;
      // The confirmation comes down before the gate goes up: what it held was composed against a precondition the
      // server has now declined to act on, and leaving it armed is how a step-up ends in a change nobody re-asked
      // for. Every other refusal leaves it standing, because the operator can still act on it — ticking the
      // acknowledgement and confirming again is the whole answer to one of them.
      if (code === 'recent_mfa_required') {
        setPending(null);
        setProofRefusal(failure.problem);
      } else if (code === 'identity_concurrency_conflict') {
        setPending(null);
        await refresh(undefined);
      }
      throw failure;
    }
  });

  // `expectedStatus` is read once, here, off the row the operator is looking at. Re-deriving it when the form is
  // submitted would make the precondition agree with whatever arrived in between — which is exactly the
  // disagreement it exists to catch (IA-REQ-054).
  const arm = (kind, row) => {
    setReason(SUSPENSION_REASONS[0]);
    setAcknowledged(false);
    setPending({ kind, identityId: row.identityId, subject: row.normalizedEmail, expectedStatus: row.accountStatus });
  };

  // Both gated states are single-object screens inside the application shell: a title on the page and one bounded
  // section under it, not the raised card the public entrance is composed of.
  if (!mayRead) {
    return (
      <Stack component="section" aria-labelledby={identitiesHeadingId} spacing={3} sx={frame}>
        <Box>
          <Typography id={identitiesHeadingId} component="h1" variant="h5">{t('identities.title')}</Typography>
        </Box>
        <Paper variant="outlined" sx={section}>
          <Typography variant="body2" color="text.secondary">
            {t('identities.accessDenied')}
          </Typography>
          <PermissionLabel code={readPermission} />
        </Paper>
      </Stack>
    );
  }

  if (owesFactor) {
    return (
      <Stack component="section" aria-labelledby={identitiesHeadingId} spacing={3} sx={frame}>
        <Box>
          <Typography id={identitiesHeadingId} component="h1" variant="h5">{t('identities.title')}</Typography>
        </Box>
        {/* Here the ceremony is the whole screen, so its submit keeps the primary weight. What was refused belongs
            with the field that produced it: above the sentence it would push the control down the page and be read
            before the instruction that explains what to do about it. */}
        <Paper variant="outlined" sx={section}>
          <Stack spacing={2}>
            <Typography variant="body2" color="text.secondary">
              {t('panel.stepUpDescription')}
            </Typography>
            <ProblemMessage problem={stepUpProblemForSummary} claimedFields={['code']} />
            <PlatformStepUpForm
              inputId={identitiesStepUpInputId}
              code={stepUp.code}
              onCodeChange={stepUp.onCodeChange}
              onSubmit={stepUp.onSubmit}
              isBusy={stepUp.isBusy}
              problem={stepUp.problem}
            />
          </Stack>
        </Paper>
      </Stack>
    );
  }

  const rows = page?.items ?? [];
  // `useSubmit` clears its problem when the next attempt starts, which is the wrong lifetime for this one: the
  // gate has to outlive the attempt and come down only once the factor is settled. So the screen holds that
  // refusal itself, and `useSubmit`'s copy of it is never the one rendered.
  const refusal = proofRefusal ?? (actionProblem?.code === 'recent_mfa_required' ? null : actionProblem);

  return (
    <Stack component="section" aria-labelledby={identitiesHeadingId} spacing={3}>
      {/* The header keeps the prescribed row even though its right slot stays empty: this screen creates no
          account, and an action invented to fill the slot would be a capability invented to fill it too. */}
      <Stack direction="row" spacing={2} sx={header}>
        <Box>
          <Typography id={identitiesHeadingId} component="h1" variant="h5">{t('identities.title')}</Typography>
        </Box>
      </Stack>

      <ProblemMessage problem={refusal} />

      {/* The gate gets its own bounded section. What it interrupts is still on screen behind it, so it has to read
          as a thing to do rather than as one more paragraph above the directory — and its submit is outlined,
          because re-proving a factor is what stands between the operator and their work, not the work itself. */}
      {proofRefusal && (
        <Paper variant="outlined" sx={section}>
          <Stack spacing={2}>
            <Typography variant="body2">
              {t('identities.recentProof')}
            </Typography>
            <ProblemMessage problem={stepUpProblemForSummary} claimedFields={['code']} />
            <PlatformStepUpForm
              inputId={identitiesStepUpInputId}
              code={stepUp.code}
              onCodeChange={stepUp.onCodeChange}
              onSubmit={stepUp.onSubmit}
              isBusy={stepUp.isBusy}
              problem={stepUp.problem}
              submitVariant={outlinedSubmitVariant}
            />
          </Stack>
        </Paper>
      )}

      {/* Both changes are confirmed rather than done on a click: each ends every session the account holds, and
          neither is undone by clicking the other one. The confirmation opens here, directly under the refusal slot
          and above the directory, so it is not something the operator has to go looking for below a long table —
          and the row it was armed from is marked while it stands. */}
      {pending?.kind === actionKinds.suspend && (
        <Paper
          variant="outlined"
          component="form"
          aria-label={t('identities.suspension.confirmationLabel')}
          sx={confirmation}
          onSubmit={(event) => {
            event.preventDefault();
            run(() => platform.suspendIdentity(pending.identityId, reason, pending.expectedStatus));
          }}
        >
          <Stack spacing={2}>
            {/* The question is the section's title and is given a title's weight. Its wording is what the operator
                is asked, so only the weight is ours to choose. */}
            <Typography component="h2" variant="subtitle1">{t('identities.suspension.prompt', { email: pending.subject })}</Typography>
            <FormControl fullWidth>
              <InputLabel id={suspensionReasonLabelId} htmlFor={suspensionReasonInputId}>{t('identities.suspension.reason')}</InputLabel>
              <Select
                labelId={suspensionReasonLabelId}
                id={suspensionReasonInputId}
                label={t('identities.suspension.reason')}
                value={reason}
                onChange={(event) => setReason(event.target.value)}
              >
                {SUSPENSION_REASONS.map((value) => <MenuItem key={value} value={value}>{t(`enums:identitySuspensionReason.${value}`)}</MenuItem>)}
              </Select>
            </FormControl>
            <Stack direction="row" spacing={1} useFlexGap sx={buttons}>
              <Button type="submit" variant="contained" color="error" disabled={isBusy}>{t('identities.suspension.confirm')}</Button>
              <Button type="button" variant="outlined" onClick={() => setPending(null)}>{t('panel.cancel')}</Button>
            </Stack>
          </Stack>
        </Paper>
      )}

      {pending?.kind === actionKinds.reactivate && (
        <Paper
          variant="outlined"
          component="form"
          aria-label={t('identities.reactivation.confirmationLabel')}
          sx={confirmation}
          onSubmit={(event) => {
            event.preventDefault();
            run(() => platform.reactivateIdentity(pending.identityId, pending.expectedStatus, acknowledged));
          }}
        >
          <Stack spacing={2}>
            <Typography component="h2" variant="subtitle1">{t('identities.reactivation.prompt', { email: pending.subject })}</Typography>
            {/* The explanation and the box it asks for are one thing, so they are one group rather than two of
                four evenly spaced paragraphs. It cannot be an `Alert`: the page-level one for the same refusal is
                already up, and this screen is read as having a single alert. Weight and colour say the same thing
                an Alert would have said about it. */}
            <Stack spacing={1}>
              {/* `invalid_platform_operation` means several different things on this screen, so the shared catalogue
                  cannot say which. On a reactivation refused with the box unticked it means exactly one of them, and
                  that is a thing the operator can act on — so the sentence is keyed on the code and the action, and
                  lives here rather than in the catalogue. */}
              {actionProblem?.code === 'invalid_platform_operation' && (
                <Typography component="p" variant="subtitle2" color="warning.main">
                  {t('identities.reactivation.acknowledgementRequired')}
                </Typography>
              )}
              {/* Unticked to begin with and set by nothing but this box. It is the operator saying they know where
                  the account will land, and no code path may say it on their behalf. */}
              <FormControlLabel
                control={(
                  <Checkbox
                    id="platform-identity-acknowledge"
                    checked={acknowledged}
                    onChange={(event) => setAcknowledged(event.target.checked)}
                  />
                )}
                label={t('identities.reactivation.acknowledgement')}
              />
            </Stack>
            <Stack direction="row" spacing={1} useFlexGap sx={buttons}>
              <Button type="submit" variant="contained" disabled={isBusy}>{t('identities.reactivation.confirm')}</Button>
              <Button type="button" variant="outlined" onClick={() => setPending(null)}>{t('panel.cancel')}</Button>
            </Stack>
          </Stack>
        </Paper>
      )}

      {/* The first read has nothing to hold but the shape of what is coming, so it holds that. The word is said to
          a reader of the status region and is not also drawn as a line of content the directory then has to
          replace: a visible "Loading…" is not a loading state. It stays inside the region rather than moving onto
          it as a label because the region is read back by its text (PlatformIdentitiesPage.test.jsx:415). */}
      {status === 'loading' && rows.length === 0 && (
        <Stack spacing={1} role="status">
          <Typography variant="body2" sx={visuallyHidden}>{t('identities.loading')}</Typography>
          {[0, 1, 2].map((placeholder) => (
            <Skeleton key={placeholder} variant="rounded" height={ACTION_ROW_HEIGHT} />
          ))}
        </Stack>
      )}

      {/* A non-retryable refusal and a retryable failure are different statements, and neither is "there is
          nothing here". The retry belongs to the message that asks for it, so the two are one block and not two. */}
      {(status === 'refused' || status === 'errored') && (
        <Stack spacing={2}>
          <ProblemMessage problem={readProblem} />
          {status === 'errored' && (
            <Button type="button" variant="outlined" sx={selfStart} onClick={() => refresh(undefined)}>
              {t('identities.retry')}
            </Button>
          )}
        </Stack>
      )}

      {status === 'loaded' && rows.length === 0 && (
        <Paper variant="outlined" sx={emptyBlock}>
          <Typography variant="body2">{t('identities.empty')}</Typography>
        </Paper>
      )}

      {rows.length > 0 && (
        <Paper variant="outlined">
          <Box sx={progressSlot}>
            {status === 'loading' && <LinearProgress aria-label={t('identities.loading')} />}
          </Box>
          <TableContainer sx={{ overflowX: { xs: 'hidden', sm: 'auto' } }}>
            <Table
              size="small"
              sx={responsiveTable(mayManage ? 760 : 640)}
            >
              <TableHead>
                <TableRow>
                  <TableCell component="th" scope="col">{t('identities.columns.address')}</TableCell>
                  <TableCell component="th" scope="col">{t('identities.columns.accountStatus')}</TableCell>
                  <TableCell component="th" scope="col">{t('identities.columns.identity')}</TableCell>
                  {/* A caller holding the read and not the manage permission is offered no transition, so the
                      column that would have carried them is not drawn empty down the edge of the directory. */}
                  {mayManage && <TableCell component="th" scope="col" align="right">{t('identities.columns.actions')}</TableCell>}
                </TableRow>
              </TableHead>
              <TableBody>
                {rows.map((row) => (
                  <TableRow key={row.identityId} hover selected={pending?.identityId === row.identityId}>
                    <TableCell data-mobile-label={t('identities.columns.address')}>
                      <Typography variant="body2" sx={mobileValue}>{row.normalizedEmail}</Typography>
                    </TableCell>
                    {/* The cell states the account status and nothing else: it is the one a journey reads back, and
                        an operator acts on the state they were shown. */}
                    <TableCell data-mobile-label={t('identities.columns.accountStatus')}>
                      <Chip
                        size="small"
                        variant="outlined"
                        label={t(`enums:accountStatus.${row.accountStatus}`)}
                        color={statusColor[row.accountStatus] ?? 'default'}
                      />
                    </TableCell>
                    {/* Rendered so an operator can copy it: it is what every other record of this account is keyed
                        by, and an address is not a stable way to name one. */}
                    <TableCell data-mobile-label={t('identities.columns.identity')}>
                      <Typography variant="caption" color="text.secondary" sx={mobileValue}>
                        {row.identityId}
                      </Typography>
                    </TableCell>
                    {mayManage && (
                      <TableCell align="right" data-mobile-label={t('identities.columns.actions')}>
                        <Stack direction="row" spacing={1} useFlexGap sx={rowActions}>
                          {offeredTransition(row.accountStatus, mayManage) === actionKinds.suspend && (
                            <Button
                              type="button"
                              variant="text"
                              size="small"
                              color="error"
                              disabled={isBusy}
                              onClick={() => arm(actionKinds.suspend, row)}
                            >
                              {t('identities.suspend', { email: row.normalizedEmail })}
                            </Button>
                          )}
                          {offeredTransition(row.accountStatus, mayManage) === actionKinds.reactivate && (
                            <Button
                              type="button"
                              variant="text"
                              size="small"
                              disabled={isBusy}
                              onClick={() => arm(actionKinds.reactivate, row)}
                            >
                              {t('identities.reactivate', { email: row.normalizedEmail })}
                            </Button>
                          )}
                        </Stack>
                      </TableCell>
                    )}
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </TableContainer>

          {/* The server answers one bounded page and names where the next one starts. Without this the directory is
              whatever the first page happened to contain, and an account past it cannot be reached at all — the
              screen offers no search either, so the cursor is the only way through. It sits inside the section it
              pages rather than floating under it, because it is a control of that table and of nothing else. */}
          {page?.nextCursor && (
            <>
              <Divider />
              <Box sx={pagerSlot}>
                <Button
                  type="button"
                  variant="outlined"
                  size="small"
                  disabled={isBusy || status === 'loading'}
                  onClick={() => refresh(page.nextCursor)}
                >
                  {t('identities.more')}
                </Button>
              </Box>
            </>
          )}
        </Paper>
      )}
    </Stack>
  );
}

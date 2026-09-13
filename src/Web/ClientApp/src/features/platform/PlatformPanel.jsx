/* eslint-disable i18next/no-literal-string -- bounded wire field names, not display copy. */
import { useCallback, useEffect, useRef, useState } from 'react';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Chip from '@mui/material/Chip';
import FormControl from '@mui/material/FormControl';
import InputLabel from '@mui/material/InputLabel';
import List from '@mui/material/List';
import ListItem from '@mui/material/ListItem';
import ListItemText from '@mui/material/ListItemText';
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
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import { useTranslation } from '../../i18n';
import { useIdentity } from '../identity/context/IdentityProvider';
import { claimedFieldNames, fieldError } from '../identity/fieldErrors';
import { ProblemMessage } from '../identity/ProblemMessage';
import { useRead } from '../identity/useRead';
import { useSubmit } from '../identity/useSubmit';
import { usePlatformClient } from './invitations/PlatformInvitationPages';
import { PlatformStepUpForm } from './shared/PlatformStepUpForm';

/**
 * The Platform panel (IA-REQ-045).
 *
 * It renders only for a session whose active tenant is Platform and which holds the matching read permission. The
 * check is for the user's benefit rather than a control — the API reauthorizes every call — but rendering a panel
 * a caller cannot use would produce a screen of refusals instead of an answer.
 *
 * Everything it shows is an allowlisted projection. There is no impersonation control, no delete action, and no
 * way to choose a tenant to act as: the acting tenant comes from the session, which is why the panel never sends
 * one (IA-REQ-046).
 */
const REASONS = ['PolicyViolation', 'SecurityIncident', 'BillingHold', 'OperatorRequest'];
const pendingActionKind = { suspend: 'suspend', revoke: 'revoke' };
const suspendedStatus = 'Suspended';
const platformAdministratorsManage = 'platform.admins.manage';
const recordedOutcome = 'recorded';
const platformStepUpInputId = 'platform-step-up';
const suspensionReasonInputId = 'platform-suspension-reason';
const suspensionReasonLabelId = 'platform-suspension-reason-label';
const outlinedVariant = 'outlined';

/** Required without the asterisk MUI would add, which would rename the field for everything that reads its label. */
const requiredField = { inputLabel: { required: false } };

/**
 * Two of the three branches are a single object — a refusal, or one form — so they cap at the standard's 560. The
 * third is three directories and takes the container it is given. Nothing here is a public auth entrance, so no
 * branch gets the raised card that treatment reserves.
 */
const frame = { maxWidth: 560 };
const supporting = { mt: 0.5, maxWidth: 640 };
const section = { p: { xs: 2, sm: 3 } };
const confirmation = { ...section, maxWidth: 560 };
const emptyBlock = { p: 4, textAlign: 'center' };
const rowActions = { justifyContent: 'flex-end', flexWrap: 'wrap' };
const buttons = { flexWrap: 'wrap', alignItems: 'center' };

/**
 * A lifecycle state is a closed set the server owns, so the colour is a lookup rather than a condition. A state
 * this panel has not been taught falls back to the neutral chip instead of being guessed at — the same set serves
 * a tenant's status, a membership's, and whether a second factor is enrolled, because all three are the server's
 * words and none of them is this screen's opinion.
 */
const statusColor = { Active: 'success', Suspended: 'warning', Revoked: 'error', Closed: 'error' };

const StatusChip = ({ status, label }) => (
  <Chip size="small" variant="outlined" label={label} color={statusColor[status] ?? 'default'} />
);

/** The wait keeps the shape of what is coming, so a directory does not arrive by pushing the page down. */
const Placeholder = () => (
  <Stack spacing={1}>
    {[0, 1, 2].map((placeholder) => <Skeleton key={placeholder} variant="rounded" height={53} />)}
  </Stack>
);

const EmptyBlock = ({ children }) => (
  <Paper variant="outlined" sx={emptyBlock}>
    <Typography variant="body2" color="text.secondary">{children}</Typography>
  </Paper>
);

export function PlatformPanel() {
  const { t } = useTranslation('platform');
  const identity = useIdentity();
  const platform = usePlatformClient();
  const [stepUpCode, setStepUpCode] = useState('');
  const [reason, setReason] = useState(REASONS[0]);
  const [inviteEmail, setInviteEmail] = useState('');
  const [inviteOutcome, setInviteOutcome] = useState(null);
  const [pendingAction, setPendingAction] = useState(null);
  const [actionTarget, setActionTarget] = useState(null);
  const { submit, clearProblem, problem: actionProblem, isBusy } = useSubmit(async (action) => action());
  const inviteEmailRef = useRef(null);
  const actionProblemFor = (target) => actionTarget === target ? actionProblem : null;
  const stepUpProblem = actionProblemFor('step-up');
  const stepUpClaimedFields = claimedFieldNames(stepUpProblem, ['code']);
  const stepUpOwnsDirectProblem = stepUpProblem?.code === 'invalid_mfa_code';
  const inviteProblem = actionProblemFor('invite');
  const inviteClaimedFields = claimedFieldNames(inviteProblem, ['email']);
  const inviteEmailError = fieldError(inviteProblem, 'email', t);
  const onStepUpCodeChange = useCallback((value) => {
    setStepUpCode(value);
    if (stepUpProblem?.code === 'invalid_mfa_code') clearProblem();
  }, [clearProblem, stepUpProblem]);

  useEffect(() => {
    if (inviteEmailError.error) inviteEmailRef.current?.focus();
  }, [inviteEmailError.error, inviteProblem]);

  const identityContext = identity?.context;
  const mayLoad = identityContext?.activeTenant?.type === 'Platform' &&
    identityContext?.session?.requiresTwoFactor !== true &&
    (identityContext?.permissions ?? []).includes('platform.organizations.read');
  // The shared hook classifies retryability from status. Each directory offers a retry for transport and server
  // failures while leaving typed non-retryable refusals as the API answered them.
  const organizations = useRead(useCallback((options) => platform.listOrganizations(options), [platform]), mayLoad);
  const administrators = useRead(useCallback((options) => platform.listAdministrators(options), [platform]), mayLoad);
  const audit = useRead(useCallback((options) => platform.listAudit(options), [platform]), mayLoad);

  const permissions = identity?.context?.permissions ?? [];
  const isPlatform = identity?.context?.activeTenant?.type === 'Platform';
  // The server refuses every directory to a session that has not proved the second factor, so the panel asks for
  // it instead of rendering three refusals (IA-REQ-045).
  const requiresStepUp = identity?.context?.session?.requiresTwoFactor === true;
  const mayRead = Boolean(identity?.isAuthenticated) && isPlatform && permissions.includes('platform.organizations.read');

  // The one sentence this branch has is not a subtitle for the title above it — it is the answer to why the panel
  // is not here, which is feedback and carries an Alert's weight. That also settles the composition: the refusal
  // IS the section, so there is no outlined card around it to frame a frame, and the gap below the heading is the
  // page-section gap because what follows is a section.
  if (!mayRead) {
    return (
      <Stack component="section" aria-labelledby="platform-panel-heading" spacing={3} sx={frame}>
        <Typography id="platform-panel-heading" component="h1" variant="h5">{t('common:navigation.platform')}</Typography>
        <Alert severity="info">{t('panel.accessDenied')}</Alert>
      </Stack>
    );
  }

  const run = (target, action, onSucceeded) => {
    setActionTarget(target);
    return submit(async () => {
    const outcome = await action();
    onSucceeded?.();
    await Promise.all([organizations.refresh(undefined), administrators.refresh(undefined), audit.refresh(undefined)]);
    return outcome;
    });
  };

  const arm = (target, pending) => {
    clearProblem();
    setActionTarget(target);
    setPendingAction(pending);
  };

  const cancelPending = () => {
    clearProblem();
    setActionTarget(null);
    setPendingAction(null);
  };

  // The one thing a session that has not proved its factor can do here, and the only thing it is offered. The
  // context is reloaded first, because whether the directories may be read is the server's answer and not this
  // form's — reading them off a stale context is how a panel starts disagreeing with the API about authority.
  if (requiresStepUp) {
    return (
      <Stack component="section" aria-labelledby="platform-panel-heading" spacing={3} sx={frame}>
        {/* The sentence explains the title rather than the form, so it belongs to the header and stays close to
            it; the section gap below separates the header from the one section on the page. */}
        <Box>
          <Typography id="platform-panel-heading" component="h1" variant="h5">{t('common:navigation.platform')}</Typography>
          <Typography variant="body2" color="text.secondary" sx={supporting}>
            {t('panel.stepUpDescription')}
          </Typography>
        </Box>
        <Paper variant="outlined" sx={section}>
          {/* A refused code is answered where the code was typed. Rendered above the title, as it was, the refusal
              sat two elements away from the field it is about. */}
          <Stack spacing={2}>
            <ProblemMessage
              problem={stepUpOwnsDirectProblem ? null : stepUpProblem}
              claimedFields={stepUpClaimedFields}
              autoFocus={stepUpClaimedFields.length === 0}
            />
            <PlatformStepUpForm
              inputId={platformStepUpInputId}
              code={stepUpCode}
              onCodeChange={onStepUpCodeChange}
              isBusy={isBusy}
              problem={stepUpProblem}
              onSubmit={() => run('step-up', async () => {
                await platform.stepUp(stepUpCode);
                await identity.reload();
              })}
            />
          </Stack>
        </Paper>
      </Stack>
    );
  }

  const organizationRows = organizations.data?.items ?? [];
  const administratorRows = administrators.data?.items ?? [];
  const auditRows = audit.data?.items ?? [];
  const armedOrganization = pendingAction?.kind === 'suspend' ? pendingAction.organization.tenantId : null;
  const armedAdministrator = pendingAction?.kind === 'revoke' ? pendingAction.administrator.membershipId : null;

  return (
    <Stack component="section" aria-labelledby="platform-panel-heading" spacing={3}>
      {/* The header is the title alone. Every action on this screen belongs to something narrower than the page —
          a row, its confirmation, the invite form's own submit — so there is nothing to right-align here that
          would not have to be invented. */}
      <Typography id="platform-panel-heading" component="h1" variant="h5">{t('common:navigation.platform')}</Typography>

      {/* A Platform change needs a recent proof of the second factor, so the panel offers one rather than
          letting the administrator discover the refusal after composing an action. It is a gate and gets its own
          bounded section: floating between the heading and the first table, it read as one more field. */}
      <Paper variant="outlined" sx={section}>
        <Stack spacing={2}>
          <ProblemMessage
            problem={stepUpOwnsDirectProblem ? null : stepUpProblem}
            claimedFields={stepUpClaimedFields}
            autoFocus={stepUpClaimedFields.length === 0}
          />
          <PlatformStepUpForm
            inputId={platformStepUpInputId}
            code={stepUpCode}
            onCodeChange={onStepUpCodeChange}
            isBusy={isBusy}
            problem={stepUpProblem}
            submitVariant={outlinedVariant}
            onSubmit={() => run('step-up', () => platform.stepUp(stepUpCode))}
          />
        </Stack>
      </Paper>

      <Stack spacing={2} aria-busy={organizations.status === 'loading'}>
        <Typography id="platform-organizations-heading" component="h2" variant="subtitle1">{t('organizations.title')}</Typography>
        <ProblemMessage problem={organizations.problem} />
        {organizations.status === 'errored' && (
          <Button type="button" variant="outlined" sx={{ alignSelf: 'flex-start' }} onClick={() => organizations.refresh(undefined)}>{t('common:actions.tryAgain')}</Button>
        )}
        {organizations.status === 'loading' && organizations.data === null ? (
          <Placeholder />
        ) : organizations.data === null ? null : organizationRows.length === 0 ? (
          <EmptyBlock>{t('organizations.empty')}</EmptyBlock>
        ) : (
          <TableContainer component={Paper} variant="outlined">
            <Table size="small" aria-labelledby="platform-organizations-heading">
              <TableHead>
                <TableRow>
                  <TableCell component="th" scope="col">{t('organizations.columns.slug')}</TableCell>
                  <TableCell component="th" scope="col">{t('organizations.columns.status')}</TableCell>
                  <TableCell component="th" scope="col">{t('organizations.columns.suspendedFor')}</TableCell>
                  <TableCell component="th" scope="col" align="right">{t('organizations.columns.actions')}</TableCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {organizationRows.map((organization) => (
                  // The armed row stays marked while its confirmation is open, so the card below the table is
                  // attached to something a reader can find again rather than to whichever row they last clicked.
                  <TableRow key={organization.tenantId} hover selected={organization.tenantId === armedOrganization}>
                    <TableCell><Typography variant="body2">{organization.slug}</Typography></TableCell>
                    <TableCell><StatusChip status={organization.status} label={t(`enums:tenantStatus.${organization.status}`)} /></TableCell>
                    {/* The reason is the server's own closed vocabulary — the same four words the picker below
                        offers — so it is read as a value, not as a sentence. */}
                    <TableCell>
                      {organization.suspensionReason ? (
                        <Chip size="small" variant="outlined" label={t(`enums:tenantSuspensionReason.${organization.suspensionReason}`)} />
                      ) : (
                        <Typography variant="body2" color="text.secondary">—</Typography>
                      )}
                    </TableCell>
                    <TableCell align="right">
                      <Stack direction="row" spacing={1} useFlexGap sx={rowActions}>
                        <ProblemMessage
                          problem={actionProblemFor(`reactivate:${organization.tenantId}`)}
                          autoFocus
                        />
                        {organization.status === suspendedStatus ? (
                          <Button type="button" size="small" disabled={isBusy} onClick={() => run(`reactivate:${organization.tenantId}`, () => platform.reactivateOrganization(organization.tenantId))}>
                            {t('organizations.reactivate', { slug: organization.slug })}
                          </Button>
                        ) : (
                          <Button type="button" size="small" color="error" disabled={isBusy} onClick={() => arm(`suspend:${organization.tenantId}`, { kind: 'suspend', organization })}>
                            {t('organizations.suspend', { slug: organization.slug })}
                          </Button>
                        )}
                      </Stack>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </TableContainer>
        )}

        {/* Suspension and revocation are confirmed rather than done on a single click: both are visible to everyone
            inside the affected tenant, and neither is undone by simply clicking again. The confirmation belongs to
            the directory it acts on, so it lives inside that section — directly under the rows, where arming it
            moves nothing a reader is looking at, rather than at the foot of the page below every other table.
            `Confirm suspension` is `outlined color="error"` rather than filled: this screen spends its one
            `contained` on inviting an administrator, the only thing here that creates something. A destructive
            step does not need fill to lead — it is the only committing control in its own card. */}
        {pendingAction?.kind === pendingActionKind.suspend && (
          <Paper
            variant="outlined"
            component="form"
            aria-label={t('suspension.confirmationLabel')}
            sx={confirmation}
            onSubmit={(event) => {
              event.preventDefault();
              const tenantId = pendingAction.organization.tenantId;
              run(`suspend:${tenantId}`, () => platform.suspendOrganization(tenantId, reason), () => setPendingAction(null));
            }}
          >
            <Stack spacing={2}>
              <Typography variant="body2">{t('suspension.prompt', { slug: pendingAction.organization.slug })}</Typography>
              <ProblemMessage
                problem={actionProblemFor(`suspend:${pendingAction.organization.tenantId}`)}
                autoFocus
              />
              <FormControl fullWidth>
                <InputLabel id={suspensionReasonLabelId} htmlFor={suspensionReasonInputId}>{t('suspension.reason')}</InputLabel>
                <Select
                  labelId={suspensionReasonLabelId}
                  id={suspensionReasonInputId}
                  label={t('suspension.reason')}
                  value={reason}
                  onChange={(event) => setReason(event.target.value)}
                >
                  {REASONS.map((value) => <MenuItem key={value} value={value}>{t(`enums:tenantSuspensionReason.${value}`)}</MenuItem>)}
                </Select>
              </FormControl>
              <Stack direction="row" spacing={1} useFlexGap sx={buttons}>
                <Button type="submit" variant="outlined" color="error" disabled={isBusy}>{t('suspension.confirm')}</Button>
                <Button type="button" onClick={cancelPending}>{t('panel.cancel')}</Button>
              </Stack>
            </Stack>
          </Paper>
        )}

        {organizations.data?.nextCursor && (
          <Button type="button" variant="outlined" disabled={organizations.status === 'loading'} sx={{ alignSelf: 'flex-start' }} onClick={() => organizations.refresh(organizations.data.nextCursor)}>{t('organizations.more')}</Button>
        )}
      </Stack>

      <Stack spacing={2} aria-busy={administrators.status === 'loading'}>
        <Typography id="platform-administrators-heading" component="h2" variant="subtitle1">{t('administrators.title')}</Typography>
        <ProblemMessage problem={administrators.problem} />
        {administrators.status === 'errored' && (
          <Button type="button" variant="outlined" sx={{ alignSelf: 'flex-start' }} onClick={() => administrators.refresh(undefined)}>{t('common:actions.tryAgain')}</Button>
        )}
        {administrators.status === 'loading' && administrators.data === null ? (
          <Placeholder />
        ) : administrators.data === null ? null : administratorRows.length === 0 ? (
          <EmptyBlock>{t('administrators.empty')}</EmptyBlock>
        ) : (
          <TableContainer component={Paper} variant="outlined">
            <Table size="small" aria-labelledby="platform-administrators-heading">
              <TableHead>
                <TableRow>
                  <TableCell component="th" scope="col">{t('administrators.columns.address')}</TableCell>
                  <TableCell component="th" scope="col">{t('administrators.columns.status')}</TableCell>
                  <TableCell component="th" scope="col">{t('administrators.columns.secondFactor')}</TableCell>
                  <TableCell component="th" scope="col" align="right">{t('administrators.columns.actions')}</TableCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {administratorRows.map((administrator) => (
                  <TableRow key={administrator.membershipId} hover selected={administrator.membershipId === armedAdministrator}>
                    <TableCell><Typography variant="body2">{administrator.normalizedEmail}</Typography></TableCell>
                    {/* The owner marker belongs to the status rather than beside it: the state and who holds the
                        Platform are read back as one string, so they stay inside one label instead of becoming two
                        chips whose text only looks joined. */}
                    <TableCell>
                      <StatusChip
                        status={administrator.membershipStatus}
                        label={administrator.isOwner ? t('administrators.owner', { status: t(`enums:membershipStatus.${administrator.membershipStatus}`) }) : t(`enums:membershipStatus.${administrator.membershipStatus}`)}
                      />
                    </TableCell>
                    <TableCell><StatusChip status={administrator.mfaStatus} label={t(`enums:mfaStatus.${administrator.mfaStatus}`)} /></TableCell>
                    <TableCell align="right">
                      <Stack direction="row" spacing={1} useFlexGap sx={rowActions}>
                        <Button type="button" size="small" color="error" disabled={isBusy} onClick={() => arm(`revoke:${administrator.membershipId}`, { kind: 'revoke', administrator })}>
                          {t('administrators.revoke', { email: administrator.normalizedEmail })}
                        </Button>
                      </Stack>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </TableContainer>
        )}

        {pendingAction?.kind === pendingActionKind.revoke && (
          <Paper
            variant="outlined"
            component="form"
            aria-label={t('revocation.confirmationLabel')}
            sx={confirmation}
            onSubmit={(event) => {
              event.preventDefault();
              const membershipId = pendingAction.administrator.membershipId;
              run(`revoke:${membershipId}`, () => platform.revokeAdministrator(membershipId), () => setPendingAction(null));
            }}
          >
            <Stack spacing={2}>
              <Typography variant="body2">{t('revocation.prompt', { email: pendingAction.administrator.normalizedEmail })}</Typography>
              <ProblemMessage
                problem={actionProblemFor(`revoke:${pendingAction.administrator.membershipId}`)}
                autoFocus
              />
              <Stack direction="row" spacing={1} useFlexGap sx={buttons}>
                <Button type="submit" variant="outlined" color="error" disabled={isBusy}>{t('revocation.confirm')}</Button>
                <Button type="button" onClick={cancelPending}>{t('panel.cancel')}</Button>
              </Stack>
            </Stack>
          </Paper>
        )}
      </Stack>

      {/* The screen's one filled button. Inviting is the only thing here that creates rather than ends something,
          it is the one action that is not begun from a row, and it sits next to the directory it adds to. */}
      {permissions.includes(platformAdministratorsManage) && (
        <Paper
          variant="outlined"
          component="form"
          aria-label={t('invite.label')}
          sx={confirmation}
          onSubmit={(event) => {
            event.preventDefault();
            if (isBusy) return;
            const submittedEmail = inviteEmail;
            setInviteOutcome(null);
            run('invite', () => platform.inviteAdministrator(submittedEmail), () => {
              setInviteOutcome('sent');
              setInviteEmail((current) => current === submittedEmail ? '' : current);
            });
          }}
        >
          <Stack spacing={2}>
            <ProblemMessage
              problem={inviteProblem}
              claimedFields={inviteClaimedFields}
              autoFocus={inviteClaimedFields.length === 0}
            />
            {inviteOutcome === 'sent' && (
              <Alert severity="success" role="status">
                {t('invite.success')}
              </Alert>
            )}
            <TextField
              id="platform-invite-email"
              label={t('invite.label')}
              type="email"
              required
              fullWidth
              slotProps={requiredField}
              inputRef={inviteEmailRef}
              {...inviteEmailError}
              value={inviteEmail}
              onChange={(event) => {
                setInviteEmail(event.target.value);
                setInviteOutcome(null);
                if (actionTarget === 'invite') clearProblem();
              }}
            />
            <Button type="submit" variant="contained" disabled={isBusy} sx={{ alignSelf: 'flex-start' }}>{t('invite.submit')}</Button>
          </Stack>
        </Paper>
      )}

      <Stack spacing={2} aria-busy={audit.status === 'loading'}>
        <Typography component="h2" variant="subtitle1">{t('audit.title')}</Typography>
        <ProblemMessage problem={audit.problem} />
        {audit.status === 'errored' && (
          <Button type="button" variant="outlined" sx={{ alignSelf: 'flex-start' }} onClick={() => audit.refresh(undefined)}>{t('common:actions.tryAgain')}</Button>
        )}
        {audit.status === 'loading' && audit.data === null ? (
          <Placeholder />
        ) : audit.data === null ? null : auditRows.length === 0 ? (
          <EmptyBlock>{t('audit.empty')}</EmptyBlock>
        ) : (
          <Paper variant="outlined">
            <List aria-label={t('audit.label')} dense disablePadding>
              {auditRows.map((event, index) => (
                <ListItem key={event.eventId} divider={index < auditRows.length - 1}>
                  <ListItemText primary={event.eventType} secondary={event.outcome ?? recordedOutcome} />
                </ListItem>
              ))}
            </List>
          </Paper>
        )}
      </Stack>
    </Stack>
  );
}

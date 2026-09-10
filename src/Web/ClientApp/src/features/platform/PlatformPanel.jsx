import { useCallback, useState } from 'react';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Chip from '@mui/material/Chip';
import FormControl from '@mui/material/FormControl';
import InputLabel from '@mui/material/InputLabel';
import List from '@mui/material/List';
import ListItem from '@mui/material/ListItem';
import ListItemText from '@mui/material/ListItemText';
import NativeSelect from '@mui/material/NativeSelect';
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
import { useIdentity } from '../identity/context/IdentityProvider';
import { ProblemMessage } from '../identity/ProblemMessage';
import { useSubmit } from '../identity/useSubmit';
import { usePlatformClient } from './invitations/PlatformInvitationPages';
import { PlatformStepUpForm } from './shared/PlatformStepUpForm';
import { usePlatformRead } from './shared/usePlatformRead';

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
  <Chip size="small" variant="outlined" label={label ?? status} color={statusColor[status] ?? 'default'} />
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
  const identity = useIdentity();
  const platform = usePlatformClient();
  const [stepUpCode, setStepUpCode] = useState('');
  const [reason, setReason] = useState(REASONS[0]);
  const [inviteEmail, setInviteEmail] = useState('');
  const [pendingAction, setPendingAction] = useState(null);
  const { submit, problem: actionProblem, isBusy } = useSubmit(async (action) => action());

  const identityContext = identity?.context;
  const mayLoad = identityContext?.activeTenant?.type === 'Platform' &&
    identityContext?.session?.requiresTwoFactor !== true &&
    (identityContext?.permissions ?? []).includes('platform.organizations.read');
  // The panel reads `page`, `problem` and `refresh`; the `status` discriminator the shared hook also returns is
  // for a screen that has to tell "no rows" apart from "you were refused", which three tables with their own
  // refusal message already do.
  const organizations = usePlatformRead(useCallback((options) => platform.listOrganizations(options), [platform]), mayLoad);
  const administrators = usePlatformRead(useCallback((options) => platform.listAdministrators(options), [platform]), mayLoad);
  const audit = usePlatformRead(useCallback((options) => platform.listAudit(options), [platform]), mayLoad);

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
        <Typography id="platform-panel-heading" component="h1" variant="h5">Platform</Typography>
        <Alert severity="info">This area is for an MFA-authenticated Platform administrator.</Alert>
      </Stack>
    );
  }

  const run = async (action) => {
    const outcome = await submit(action);
    if (outcome !== undefined) {
      setPendingAction(null);
      await Promise.all([organizations.refresh(undefined), administrators.refresh(undefined), audit.refresh(undefined)]);
    }
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
          <Typography id="platform-panel-heading" component="h1" variant="h5">Platform</Typography>
          <Typography variant="body2" color="text.secondary" sx={supporting}>
            This session has not proved your second factor yet. Enter a code from your authenticator to continue.
          </Typography>
        </Box>
        <Paper variant="outlined" sx={section}>
          {/* A refused code is answered where the code was typed. Rendered above the title, as it was, the refusal
              sat two elements away from the field it is about. */}
          <Stack spacing={2}>
            <ProblemMessage problem={actionProblem} />
            <PlatformStepUpForm
              inputId="platform-step-up"
              code={stepUpCode}
              onCodeChange={setStepUpCode}
              isBusy={isBusy}
              onSubmit={() => run(async () => {
                await platform.stepUp(stepUpCode);
                await identity.reload();
              })}
            />
          </Stack>
        </Paper>
      </Stack>
    );
  }

  const organizationRows = organizations.page?.items ?? [];
  const administratorRows = administrators.page?.items ?? [];
  const auditRows = audit.page?.items ?? [];
  const armedOrganization = pendingAction?.kind === 'suspend' ? pendingAction.organization.tenantId : null;
  const armedAdministrator = pendingAction?.kind === 'revoke' ? pendingAction.administrator.membershipId : null;

  return (
    <Stack component="section" aria-labelledby="platform-panel-heading" spacing={3}>
      {/* The header is the title alone. Every action on this screen belongs to something narrower than the page —
          a row, its confirmation, the invite form's own submit — so there is nothing to right-align here that
          would not have to be invented. */}
      <Typography id="platform-panel-heading" component="h1" variant="h5">Platform</Typography>
      <ProblemMessage problem={actionProblem} />

      {/* A Platform change needs a recent proof of the second factor, so the panel offers one rather than
          letting the administrator discover the refusal after composing an action. It is a gate and gets its own
          bounded section: floating between the heading and the first table, it read as one more field. */}
      <Paper variant="outlined" sx={section}>
        <PlatformStepUpForm
          inputId="platform-step-up"
          code={stepUpCode}
          onCodeChange={setStepUpCode}
          isBusy={isBusy}
          submitVariant="outlined"
          onSubmit={() => run(() => platform.stepUp(stepUpCode))}
        />
      </Paper>

      <Stack spacing={2}>
        <Typography id="platform-organizations-heading" component="h2" variant="subtitle1">Organizations</Typography>
        <ProblemMessage problem={organizations.problem} />
        {organizations.problem !== null ? null : organizations.page === null ? (
          <Placeholder />
        ) : organizationRows.length === 0 ? (
          <EmptyBlock>No organizations are listed here.</EmptyBlock>
        ) : (
          <TableContainer component={Paper} variant="outlined">
            <Table size="small" aria-labelledby="platform-organizations-heading">
              <TableHead>
                <TableRow>
                  <TableCell component="th" scope="col">Slug</TableCell>
                  <TableCell component="th" scope="col">Status</TableCell>
                  <TableCell component="th" scope="col">Suspended for</TableCell>
                  <TableCell component="th" scope="col" align="right">Actions</TableCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {organizationRows.map((organization) => (
                  // The armed row stays marked while its confirmation is open, so the card below the table is
                  // attached to something a reader can find again rather than to whichever row they last clicked.
                  <TableRow key={organization.tenantId} hover selected={organization.tenantId === armedOrganization}>
                    <TableCell><Typography variant="body2">{organization.slug}</Typography></TableCell>
                    <TableCell><StatusChip status={organization.status} /></TableCell>
                    {/* The reason is the server's own closed vocabulary — the same four words the picker below
                        offers — so it is read as a value, not as a sentence. */}
                    <TableCell>
                      {organization.suspensionReason ? (
                        <Chip size="small" variant="outlined" label={organization.suspensionReason} />
                      ) : (
                        <Typography variant="body2" color="text.secondary">—</Typography>
                      )}
                    </TableCell>
                    <TableCell align="right">
                      <Stack direction="row" spacing={1} useFlexGap sx={rowActions}>
                        {organization.status === 'Suspended' ? (
                          <Button type="button" size="small" disabled={isBusy} onClick={() => run(() => platform.reactivateOrganization(organization.tenantId))}>
                            {`Reactivate ${organization.slug}`}
                          </Button>
                        ) : (
                          <Button type="button" size="small" color="error" disabled={isBusy} onClick={() => setPendingAction({ kind: 'suspend', organization })}>
                            {`Suspend ${organization.slug}`}
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
        {pendingAction?.kind === 'suspend' && (
          <Paper
            variant="outlined"
            component="form"
            aria-label="Confirm suspension"
            sx={confirmation}
            onSubmit={(event) => { event.preventDefault(); run(() => platform.suspendOrganization(pendingAction.organization.tenantId, reason)); }}
          >
            <Stack spacing={2}>
              <Typography variant="body2">{`Suspend ${pendingAction.organization.slug}?`}</Typography>
              <FormControl fullWidth>
                <InputLabel htmlFor="platform-suspension-reason">Reason</InputLabel>
                <NativeSelect
                  inputProps={{ id: 'platform-suspension-reason' }}
                  value={reason}
                  onChange={(event) => setReason(event.target.value)}
                >
                  {REASONS.map((value) => <option key={value} value={value}>{value}</option>)}
                </NativeSelect>
              </FormControl>
              <Stack direction="row" spacing={1} useFlexGap sx={buttons}>
                <Button type="submit" variant="outlined" color="error" disabled={isBusy}>Confirm suspension</Button>
                <Button type="button" onClick={() => setPendingAction(null)}>Cancel</Button>
              </Stack>
            </Stack>
          </Paper>
        )}

        {organizations.page?.nextCursor && (
          <Button type="button" variant="outlined" sx={{ alignSelf: 'flex-start' }} onClick={() => organizations.refresh(organizations.page.nextCursor)}>More organizations</Button>
        )}
      </Stack>

      <Stack spacing={2}>
        <Typography id="platform-administrators-heading" component="h2" variant="subtitle1">Administrators</Typography>
        <ProblemMessage problem={administrators.problem} />
        {administrators.problem !== null ? null : administrators.page === null ? (
          <Placeholder />
        ) : administratorRows.length === 0 ? (
          <EmptyBlock>No administrators are listed here.</EmptyBlock>
        ) : (
          <TableContainer component={Paper} variant="outlined">
            <Table size="small" aria-labelledby="platform-administrators-heading">
              <TableHead>
                <TableRow>
                  <TableCell component="th" scope="col">Address</TableCell>
                  <TableCell component="th" scope="col">Status</TableCell>
                  <TableCell component="th" scope="col">Second factor</TableCell>
                  <TableCell component="th" scope="col" align="right">Actions</TableCell>
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
                        label={administrator.isOwner ? `${administrator.membershipStatus} (owner)` : administrator.membershipStatus}
                      />
                    </TableCell>
                    <TableCell><StatusChip status={administrator.mfaStatus} /></TableCell>
                    <TableCell align="right">
                      <Stack direction="row" spacing={1} useFlexGap sx={rowActions}>
                        <Button type="button" size="small" color="error" disabled={isBusy} onClick={() => setPendingAction({ kind: 'revoke', administrator })}>
                          {`Revoke ${administrator.normalizedEmail}`}
                        </Button>
                      </Stack>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </TableContainer>
        )}

        {pendingAction?.kind === 'revoke' && (
          <Paper
            variant="outlined"
            component="form"
            aria-label="Confirm revocation"
            sx={confirmation}
            onSubmit={(event) => { event.preventDefault(); run(() => platform.revokeAdministrator(pendingAction.administrator.membershipId)); }}
          >
            <Stack spacing={2}>
              <Typography variant="body2">{`Revoke ${pendingAction.administrator.normalizedEmail}?`}</Typography>
              <Stack direction="row" spacing={1} useFlexGap sx={buttons}>
                <Button type="submit" variant="outlined" color="error" disabled={isBusy}>Confirm revocation</Button>
                <Button type="button" onClick={() => setPendingAction(null)}>Cancel</Button>
              </Stack>
            </Stack>
          </Paper>
        )}
      </Stack>

      {/* The screen's one filled button. Inviting is the only thing here that creates rather than ends something,
          it is the one action that is not begun from a row, and it sits next to the directory it adds to. */}
      {permissions.includes('platform.admins.manage') && (
        <Paper
          variant="outlined"
          component="form"
          aria-label="Invite an administrator"
          sx={confirmation}
          onSubmit={(event) => { event.preventDefault(); run(() => platform.inviteAdministrator(inviteEmail)); }}
        >
          <Stack spacing={2}>
            <TextField
              id="platform-invite-email"
              label="Invite an administrator"
              type="email"
              required
              fullWidth
              slotProps={requiredField}
              value={inviteEmail}
              onChange={(event) => setInviteEmail(event.target.value)}
            />
            <Button type="submit" variant="contained" disabled={isBusy} sx={{ alignSelf: 'flex-start' }}>Invite</Button>
          </Stack>
        </Paper>
      )}

      <Stack spacing={2}>
        <Typography component="h2" variant="subtitle1">Audit</Typography>
        <ProblemMessage problem={audit.problem} />
        {audit.problem !== null ? null : audit.page === null ? (
          <Placeholder />
        ) : auditRows.length === 0 ? (
          <EmptyBlock>Nothing has been recorded here yet.</EmptyBlock>
        ) : (
          <Paper variant="outlined">
            <List aria-label="Audit" dense disablePadding>
              {auditRows.map((event, index) => (
                <ListItem key={event.eventId} divider={index < auditRows.length - 1}>
                  <ListItemText primary={event.eventType} secondary={event.outcome ?? 'recorded'} />
                </ListItem>
              ))}
            </List>
          </Paper>
        )}
      </Stack>
    </Stack>
  );
}

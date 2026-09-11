import { useCallback, useEffect, useState } from 'react';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Checkbox from '@mui/material/Checkbox';
import Chip from '@mui/material/Chip';
import CircularProgress from '@mui/material/CircularProgress';
import FormControl from '@mui/material/FormControl';
import FormControlLabel from '@mui/material/FormControlLabel';
import FormGroup from '@mui/material/FormGroup';
import FormLabel from '@mui/material/FormLabel';
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
import { useIdentity } from '../context/IdentityProvider';
import { ProblemMessage } from '../ProblemMessage';
import { roleName, useFormat, useTranslation } from '../../../i18n';

/** Required without the asterisk MUI would add, which would rename the field for everything that reads its label. */
const requiredField = { inputLabel: { required: false } };

const header = { alignItems: 'flex-start', justifyContent: 'space-between', flexWrap: 'wrap' };
const supporting = { mt: 0.5, maxWidth: 640 };

/**
 * One column, and the same one for the form and for the answer the form gets back. The cap lives here rather than
 * on the `Paper` so that a confirmation, a refusal and the two fields they are about are all the same width: an
 * alert stretched across the shell to say something about a two-field form is wider than the thing it reports on.
 */
const compose = { maxWidth: 560 };
const form = { p: { xs: 2, sm: 3 } };
const empty = { p: 4, textAlign: 'center' };
const refusal = { p: 2 };
const note = { mt: 1 };
const chips = { flexWrap: 'wrap' };
const rowActions = { flexWrap: 'wrap', justifyContent: 'flex-end' };
const start = { alignSelf: 'flex-start' };
const pendingAction = {
  send: 'send',
  more: 'more',
  resend: (invitationId) => `resend-${invitationId}`,
  withdraw: (invitationId) => `withdraw-${invitationId}`,
};
const invitationStatus = { pending: 'Pending' };

/**
 * The wait belongs to the control that started it. `isBusy` disables everything, because any of these writes
 * reloads the list underneath the rest, but only the pressed button shows the spinner — four spinners for one
 * request would say that four things are happening. It carries no role: this screen resolves `role="status"` as a
 * single element and that one is spoken for by the confirmation.
 */
const spinner = (busy) => (busy ? <CircularProgress size={16} color="inherit" /> : null);

/**
 * An offer's state is a closed set the server owns, so the colour is a lookup rather than a condition. A standing
 * offer is neutral because nothing has happened to it yet, and a state this screen has not been taught falls back
 * to the same neutral chip instead of being guessed at.
 */
const statusColor = { Accepted: 'success', Cancelled: 'error', Expired: 'warning' };

/**
 * Offering somebody a place in the organization, and everything that can still happen to that offer
 * (IA-REQ-015/017/018).
 *
 * Nothing here ever shows a token. Issuing answers with the invitation's identifier and expiry, reissuing
 * rotates the token inside the recipient's envelope and answers with nothing at all, and withdrawing ends the
 * offer — so the only credential involved reaches the recipient by email and exists nowhere on this screen.
 *
 * Roles are chosen by name from the organization's own catalogue rather than typed as identifiers: an inviter
 * may only offer what they could grant, and a screen that asks for a raw identifier makes that impossible to
 * see. Listing offers needs `members.read` and the catalogue needs `roles.read`, which an inviter may not hold,
 * so each part is loaded on its own and its absence is said plainly instead of failing the page.
 */
export function InviteMemberPage() {
  const { formatDate } = useFormat();
  const identity = useIdentity();
  const { t } = useTranslation();
  const tenantId = identity.context?.activeTenant?.id ?? null;
  // Three answers, not two: `undefined` is "not asked yet" and holds the shape of what is coming, `null` is the
  // refusal this screen says out loud, and an array is the part of the organization this inviter may read.
  const [roles, setRoles] = useState(undefined);
  const [invitations, setInvitations] = useState(undefined);
  const [nextCursor, setNextCursor] = useState(null);
  const [roleIds, setRoleIds] = useState([]);
  const [email, setEmail] = useState('');
  const [problem, setProblem] = useState(null);
  const [sent, setSent] = useState(null);
  // What is running, not merely that something is. Every control still waits for whatever it is — a resend
  // reloads the list the other rows are drawn from — but the wait is shown where it was asked for.
  const [pending, setPending] = useState(null);
  const isBusy = pending !== null;

  // A refused list is answered with null rather than thrown: an inviter may hold `members.invite` without
  // `roles.read` or `members.read`, and losing the whole screen over a part of it they were never promised
  // would be this component inventing a rule the server did not state.
  const read = useCallback(async () => {
    if (tenantId === null) return null;
    const [available, offered] = await Promise.all([
      identity.client.listRoles(tenantId).then((page) => page.items.filter((role) => !role.isRetired), () => null),
      identity.client.listTenantInvitations(tenantId).then((page) => page, () => null),
    ]);
    // `null` keeps meaning "you may not see the offers here", so only a page that really arrived carries a
    // cursor: a refused read must not leave a continuation control pointing at nothing.
    return { roles: available, invitations: offered?.items ?? null, cursor: offered?.nextCursor ?? null };
  }, [identity, tenantId]);

  const load = useCallback(async () => {
    const state = await read();
    if (state === null) return;
    setRoles(state.roles);
    setInvitations(state.invitations);
    setNextCursor(state.cursor);
  }, [read]);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      const state = await read();
      if (cancelled || state === null) return;
      setRoles(state.roles);
      setInvitations(state.invitations);
      setNextCursor(state.cursor);
    })();
    return () => { cancelled = true; };
  }, [read]);

  const run = async (token, act) => {
    setPending(token);
    setProblem(null);
    try {
      const value = await act();
      await load();
      return value;
    } catch (error) {
      setProblem(error.problem ?? { code: 'unexpected' });
      return undefined;
    } finally {
      setPending(null);
    }
  };

  const invite = async (event) => {
    event.preventDefault();
    setSent(null);
    const issued = await run(pendingAction.send, () => identity.client.inviteMember(tenantId, email, roleIds));
    if (issued) {
      setSent(issued);
      setEmail('');
      setRoleIds([]);
    }
  };

  // A continuation appends, so every offer the reader has seen stays on screen. Each page the server hands out
  // is disjoint from the last, so an offer cannot be listed twice.
  const showMore = async () => {
    setPending(pendingAction.more);
    setProblem(null);
    try {
      const next = await identity.client.listTenantInvitations(tenantId, nextCursor);
      setInvitations((current) => [...(current ?? []), ...next.items]);
      setNextCursor(next.nextCursor ?? null);
    } catch (error) {
      setProblem(error.problem ?? { code: 'unexpected' });
    } finally {
      setPending(null);
    }
  };

  const toggleRole = (roleId) => setRoleIds((current) =>
    current.includes(roleId) ? current.filter((held) => held !== roleId) : [...current, roleId]);

  // A dead end rather than a failure, and it is still this screen: the same title and the sentence that says what
  // is missing. It is framed as the single-object screen it is — the raised card belongs to the public entrance,
  // and this route renders inside the shell, where a full-width elevated slab holding two sentences claims the
  // whole page for the least of them.
  if (tenantId === null) {
    return (
      <Stack component="section" aria-labelledby="invite-heading" spacing={3} sx={compose}>
        <Box>
          <Typography id="invite-heading" component="h1" variant="h5">{t('identity:invitations.member.title')}</Typography>
          <Typography variant="body2" color="text.secondary" sx={supporting}>
            {t('identity:invitations.member.noOrganization')}
          </Typography>
        </Box>
        {/* A dead end with exactly one way out, and no control offering it: the label would be a user-visible
            string this screen has never carried, so it is reported rather than written. */}
      </Stack>
    );
  }

  const nameOf = (roleId) => {
    const role = roles?.find((candidate) => candidate.roleId === roleId);
    return role ? roleName(role, t) : roleId;
  };

  return (
    <Stack component="section" aria-labelledby="invite-heading" spacing={3}>
      {/* The header row keeps its right-hand slot empty on purpose. This screen's primary action is the form's own
          submit, and a second control carrying that same name would be two buttons answering to one name for
          everything that finds a button by what it says. The title stays inside a `Box` so that anything the screen
          ever has to say beneath it is a sibling of the heading and never part of it. */}
      <Stack direction="row" spacing={2} sx={header}>
        <Box>
          <Typography id="invite-heading" component="h1" variant="h5">{t('identity:invitations.member.title')}</Typography>
        </Box>
      </Stack>

      {/* What the form is told belongs to the form. The confirmation names an expiry for the address in the field
          above it, and a refusal is about the request that field just made, so both share the form's column
          instead of being announced across the whole shell. */}
      <Stack spacing={2} sx={compose}>
        <ProblemMessage problem={problem} />
        {sent && (
          <Alert severity="success" role="status">
            {t('identity:invitations.member.sent', { expiresAt: formatDate(sent.expiresAt) })}
          </Alert>
        )}

        <Paper variant="outlined" component="form" onSubmit={invite} sx={form}>
          <Stack spacing={2}>
            <TextField
              id="invite-email"
              label={t('identity:login.email')}
              type="email"
              required
              fullWidth
              slotProps={requiredField}
              value={email}
              onChange={(event) => setEmail(event.target.value)}
            />

            <FormControl component="fieldset">
              <FormLabel component="legend">{t('identity:invitations.member.rolesToOffer')}</FormLabel>
              {roles === undefined && (
                <Stack spacing={1} sx={note}>
                  {[0, 1].map((placeholder) => <Skeleton key={placeholder} variant="rounded" height={38} />)}
                </Stack>
              )}
              {/* A refusal and an empty catalogue are different facts and must not read alike. What this reader
                  may not see is said at its own weight; an organization that simply has no roles yet stays a
                  quiet aside, because that one is about the organization rather than about them. */}
              {roles === null && (
                <Typography component="p" variant="subtitle2" sx={note}>
                  {t('identity:invitations.member.rolesRefused')}
                </Typography>
              )}
              {roles?.length === 0 && (
                <Typography variant="body2" color="text.secondary" sx={note}>{t('identity:invitations.member.noRolesToOffer')}</Typography>
              )}
              <FormGroup>
                {roles?.map((role) => (
                  <FormControlLabel
                    key={role.roleId}
                    htmlFor={`invite-role-${role.roleId}`}
                    control={(
                      <Checkbox
                        id={`invite-role-${role.roleId}`}
                        checked={roleIds.includes(role.roleId)}
                        onChange={() => toggleRole(role.roleId)}
                      />
                    )}
                    label={roleName(role, t)}
                  />
                ))}
              </FormGroup>
            </FormControl>

            <Button
              type="submit"
              variant="contained"
              disabled={isBusy}
              startIcon={spinner(pending === pendingAction.send)}
              sx={start}
            >
              {t('identity:invitations.member.send')}
            </Button>
          </Stack>
        </Paper>
      </Stack>

      <Stack spacing={2}>
        {/* A section under an `h5` page title, so it is a section's weight. The level stays h2 — what changes is
            how loudly it is set, not where it sits in the outline. */}
        <Typography component="h2" variant="subtitle1">{t('identity:invitations.member.listTitle')}</Typography>

        {/* The wait holds the shape of the offers rather than saying a word about itself, so the list does not
            arrive by pushing the form up the page. */}
        {invitations === undefined ? (
          <Stack spacing={1}>
            {[0, 1, 2].map((placeholder) => <Skeleton key={placeholder} variant="rounded" height={57} />)}
          </Stack>
        ) : invitations === null ? (
          // Deliberately not the empty block. An organization that has never invited anybody and an organization
          // whose offers this reader may not see are two different facts, and the centred, quiet block that says
          // "there is nothing here" would make them look like one. The refusal is set left, tight and at its own
          // weight: it is a statement about the reader, not about the list.
          <Paper variant="outlined" sx={refusal}>
            <Typography component="p" variant="subtitle2">{t('identity:invitations.member.invitationsRefused')}</Typography>
          </Paper>
        ) : invitations.length === 0 ? (
          <Paper variant="outlined" sx={empty}>
            <Typography variant="body2" color="text.secondary">{t('identity:invitations.member.noInvitations')}</Typography>
          </Paper>
        ) : (
          <TableContainer component={Paper} variant="outlined">
            <Table size="small">
              <TableHead>
                <TableRow>
                  <TableCell component="th" scope="col">{t('identity:invitations.member.recipient')}</TableCell>
                  <TableCell component="th" scope="col">{t('identity:invitations.member.state')}</TableCell>
                  <TableCell component="th" scope="col">{t('identity:invitations.member.roles')}</TableCell>
                  <TableCell component="th" scope="col" align="right">{t('identity:invitations.member.actions')}</TableCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {invitations.map((invitation) => (
                  <TableRow key={invitation.invitationId} hover>
                    <TableCell>
                      <Typography variant="body2">{invitation.normalizedEmail}</Typography>
                      <Typography component="div" variant="caption" color="text.secondary">
                        {t('identity:invitations.member.expires', { expiresAt: formatDate(invitation.expiresAt) })}
                      </Typography>
                    </TableCell>
                    <TableCell>
                      <Chip
                        size="small"
                        variant="outlined"
                        label={t(`enums:invitationStatus.${invitation.status}`)}
                        color={statusColor[invitation.status] ?? 'default'}
                      />
                    </TableCell>
                    <TableCell>
                      {invitation.roleIds.length === 0 ? (
                        <Typography variant="caption" color="text.secondary">{t('identity:invitations.member.noRoles')}</Typography>
                      ) : (
                        <Stack direction="row" spacing={0.5} useFlexGap sx={chips}>
                          {invitation.roleIds.map((roleId) => <Chip key={roleId} size="small" label={nameOf(roleId)} />)}
                        </Stack>
                      )}
                    </TableCell>
                    <TableCell align="right">
                      {/* Only a standing offer can be reissued or withdrawn. One already accepted or already withdrawn
                          is shown because it happened, not because there is anything left to do to it. */}
                      <Stack direction="row" spacing={1} useFlexGap sx={rowActions}>
                        {invitation.status === invitationStatus.pending && (
                          <>
                            <Button
                              type="button"
                              size="small"
                              disabled={isBusy}
                              startIcon={spinner(pending === pendingAction.resend(invitation.invitationId))}
                              onClick={() => run(
                                pendingAction.resend(invitation.invitationId),
                                () => identity.client.resendInvitation(tenantId, invitation.invitationId),
                              )}
                            >
                              {t('identity:invitations.member.resend', { email: invitation.normalizedEmail })}
                            </Button>
                            <Button
                              type="button"
                              size="small"
                              color="error"
                              disabled={isBusy}
                              startIcon={spinner(pending === pendingAction.withdraw(invitation.invitationId))}
                              onClick={() => {
                                // The browser's own confirmation, deliberately: ending somebody's way in is asked
                                // for by the browser rather than by the page.
                                if (window.confirm(t('identity:invitations.member.withdrawConfirm', { email: invitation.normalizedEmail }))) {
                                  run(
                                    pendingAction.withdraw(invitation.invitationId),
                                    () => identity.client.cancelInvitation(tenantId, invitation.invitationId),
                                  );
                                }
                              }}
                            >
                              {t('identity:invitations.member.withdraw', { email: invitation.normalizedEmail })}
                            </Button>
                          </>
                        )}
                      </Stack>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </TableContainer>
        )}

        {nextCursor !== null && (
          <Button
            type="button"
            variant="outlined"
            disabled={isBusy}
            startIcon={spinner(pending === pendingAction.more)}
            onClick={showMore}
            sx={start}
          >
            {t('identity:invitations.member.showMore')}
          </Button>
        )}
      </Stack>
    </Stack>
  );
}

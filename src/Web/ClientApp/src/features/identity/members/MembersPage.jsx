import { useCallback, useEffect, useState } from 'react';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Checkbox from '@mui/material/Checkbox';
import Chip from '@mui/material/Chip';
import Divider from '@mui/material/Divider';
import FormControl from '@mui/material/FormControl';
import FormControlLabel from '@mui/material/FormControlLabel';
import FormGroup from '@mui/material/FormGroup';
import FormLabel from '@mui/material/FormLabel';
import List from '@mui/material/List';
import ListItem from '@mui/material/ListItem';
import Paper from '@mui/material/Paper';
import Skeleton from '@mui/material/Skeleton';
import Stack from '@mui/material/Stack';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import { toProblem } from '../api/apiTransport';
import { useIdentity } from '../context/IdentityProvider';
import { ProblemMessage } from '../ProblemMessage';
import { useIdentityProof } from '../useIdentityProof';
import { useRead } from '../useRead';
import { roleName, useTranslation } from '../../../i18n';

const frame = { maxWidth: 560 };
const panel = { p: { xs: 2, sm: 3 }, maxWidth: 560 };
const empty = { p: 4, textAlign: 'center' };
const supporting = { mt: 0.5, maxWidth: 640 };
const selfStart = { alignSelf: 'flex-start' };
const rowActions = {
  alignSelf: { md: 'center' },
  flexWrap: 'wrap',
  justifyContent: { xs: 'flex-start', md: 'flex-end' },
};
const row = { flexWrap: 'wrap', alignItems: 'center' };

/**
 * A member row is a block rather than a table row, so its height is the sum of what it holds: the list's own
 * 16px above and below, the name at 22px, the address at 20px under it, the 12px the row's inner `Stack` puts
 * between that block and the line of actions, a `size="small"` button at 31px, and the 1px rule to the next
 * member. The wait is drawn at that height so the roster arrives into space already held for it rather than
 * pushing the page down. (The 57 this replaces was a table row's height, from before the roster was a list.)
 */
const ROW_HEIGHT = 16 + 22 + 20 + 12 + 31 + 16 + 1;

/**
 * An absence is not a value, so it is not drawn as one — but it stands where the values stand. A `Chip
 * size="small"` is 24px tall and this caption's own line box is 20, so 2px above and below puts "no roles" on
 * exactly the line the chips beside it sit on instead of floating between them.
 */
const absence = { py: 0.25 };

/**
 * A member is one row, laid out as a block rather than as the flex line `ListItem` ships: the identity, what the
 * organization has granted it, what may be done to it, and the role editor it opens all belong to the same person
 * and therefore to the same element. Keeping them inside one `li` is also what lets a caller scope a query to a
 * member and find everything about them — a table would put the editor in a sibling row instead.
 */
const memberRow = { display: 'block', minHeight: 44, py: 2 };
const memberLayout = {
  display: 'grid',
  gridTemplateColumns: { xs: 'minmax(0, 1fr)', md: 'minmax(0, 1fr) auto' },
  gap: 1.5,
};
const memberHead = {
  display: 'grid',
  gridTemplateColumns: { xs: 'minmax(0, 1fr)', sm: 'minmax(180px, 0.8fr) minmax(0, 1.2fr)' },
  gap: 1,
  alignItems: 'start',
};
const nameBlock = { minWidth: 0 };
const nameLine = { alignItems: 'baseline', flexWrap: 'wrap' };
const badges = { alignItems: 'center', flexWrap: 'wrap' };
const fullMemberRow = { gridColumn: '1 / -1' };
const transferRow = { ...fullMemberRow, justifySelf: { xs: 'stretch', md: 'end' } };
const editor = { ...fullMemberRow, pt: 1 };

/**
 * A membership state is a closed set the server owns, so the colour is a lookup rather than a condition. An
 * unknown state falls back to the neutral chip instead of guessing: a state this screen has not been taught is
 * not an error, it is a state this screen has not been taught.
 */
const statusColor = { Active: 'success', Suspended: 'warning', Revoked: 'error' };
const memberStatus = { active: 'Active', suspended: 'Suspended', revoked: 'Revoked' };
const memberStatusAction = { suspend: 'suspend', reactivate: 'reactivate', revoke: 'revoke' };
const memberRoleInputId = (membershipId, roleId) => `role-${membershipId}-${roleId}`;
const memberReadTarget = { roster: 'roster', pagination: 'pagination' };
const appendMembers = (current, loaded) => ({
  ...loaded,
  items: [...current.items, ...loaded.items],
});

/**
 * The people in the organization the session is operating in, and what may be done to them (IA-REQ-053).
 *
 * Three rules shape this screen and all three belong to the server. **The ceiling** — you may only hand somebody
 * a role you could have built yourself — is why the roles offered come from the same catalogue the roles screen
 * uses. **The floor** — an organization always keeps an administrator — is why a refusal is shown rather than
 * predicted. And **the owner's own membership cannot be ended**: an organization whose owner is not a member has
 * nobody who can give it away, so the way out is to transfer first.
 *
 * Changing a member's roles and transferring ownership each buy a proof (amendment D2); suspending, reactivating
 * and revoking echo the row's own version instead. Which proof depends on what the identity has — a password
 * cleared the moment it is used, or a round trip to the provider it signed in with (IA-REQ-025, IA-REQ-051).
 *
 * One read is required here and the rest are courtesies. `members.read` alone is enough to be handed the roster,
 * so only the roster's own failure is this screen's failure; the role catalogue that turns identifiers into
 * names is a separate permission, and a refusal of it is said where the names would have been.
 */
const MembersPath = '/members';

export function MembersPage() {
  const identity = useIdentity();
  const { t } = useTranslation();
  const proof = useIdentityProof();
  const tenantId = identity.context?.activeTenant?.id ?? null;
  const [password, setPassword] = useState('');
  const [actionProblem, setActionProblem] = useState(null);
  const [actionTarget, setActionTarget] = useState(null);
  const [readTarget, setReadTarget] = useState('roster');
  const [isBusy, setIsBusy] = useState(false);
  const [editing, setEditing] = useState(null);

  const roster = useRead(
    useCallback(
      ({ cursor, signal }) => identity.client.listMembers(tenantId, cursor, { signal }),
      [identity.client, tenantId],
    ),
    tenantId !== null,
  );
  const catalog = useRead(
    useCallback(async ({ signal }) => {
      const page = await identity.client.listRoles(tenantId, null, { signal });
      return page.items.filter((role) => !role.isRetired);
    }, [identity.client, tenantId]),
    tenantId !== null,
  );
  const members = roster.data?.items ?? null;
  const nextCursor = roster.data?.nextCursor ?? null;
  const roles = catalog.data;

  const run = async (target, action, act, intent = null) => {
    setIsBusy(true);
    setActionTarget(target);
    setActionProblem(null);
    try {
      // A provider proof leaves for the provider rather than answering, so the change waits for the round trip.
      if (action !== null && !await proof.prove(action, password, intent)) return;
      await act();
      setPassword('');
      setEditing(null);
      setReadTarget('roster');
      await Promise.all([roster.refresh(undefined), catalog.refresh(undefined)]);
    } catch (error) {
      setActionProblem(toProblem(error));
    } finally {
      setIsBusy(false);
    }
  };

  const saveRoles = (member) => run(
    `roles:${member.membershipId}`,
    'members.roles.change',
    () => identity.client.updateMemberRoles(tenantId, member.membershipId, editing.roleIds, member.version),
    { returnTo: MembersPath, operation: 'roles', target: member.membershipId, draft: { roleIds: editing.roleIds, version: member.version } });

  const changeStatus = (member, change) =>
    run(`member:${member.membershipId}`, null, () => identity.client.changeMemberStatus(tenantId, member.membershipId, change, member.version));

  const transfer = (member) => run(
    `member:${member.membershipId}`,
    'tenant.ownership.transfer',
    () => identity.client.transferOwnership(tenantId, member.membershipId, member.version),
    { returnTo: MembersPath, operation: 'transfer', target: member.membershipId, draft: { version: member.version } });

  // Resumed once the server accepted the round trip, against the roster as it stands now: the member has to
  // still be there, and for a transfer still be somebody the organization can be handed to.
  const waiting = proof.resumable(MembersPath);
  useEffect(() => {
    if (waiting === null || members === null || !proof.isReady) return;
    let cancelled = false;
    void Promise.resolve().then(async () => {
      if (cancelled) return;
      setIsBusy(true);
      const target = waiting.operation === 'roles'
        ? `roles:${waiting.target}`
        : `member:${waiting.target}`;
      setActionTarget(target);
      setActionProblem(null);
      try {
        // A fresh return loads only page one. Follow its current cursors before deciding the member is gone.
        let member = members.find((candidate) => candidate.membershipId === waiting.target);
        let cursor = nextCursor;
        while (member === undefined && cursor !== null) {
          const page = await identity.client.listMembers(tenantId, cursor);
          if (cancelled) return;
          member = page.items.find((candidate) => candidate.membershipId === waiting.target);
          cursor = page.nextCursor ?? null;
        }
        if (cancelled || proof.resumable(MembersPath) !== waiting) return;
        proof.forget();
        if (member === undefined) return;
        const pending = waiting.draft ?? {};
        if (waiting.operation === 'roles') {
          await run(target, null, () => identity.client.updateMemberRoles(tenantId, member.membershipId, pending.roleIds, pending.version));
        } else if (waiting.operation === 'transfer' && !member.isOwner && member.status === 'Active') {
          await run(target, null, () => identity.client.transferOwnership(tenantId, member.membershipId, pending.version));
        }
      } catch (error) {
        if (!cancelled) setActionProblem(toProblem(error));
      } finally {
        if (!cancelled) setIsBusy(false);
      }
    });
    return () => { cancelled = true; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [waiting, members, nextCursor, proof.isReady, tenantId]);

  // A continuation appends. Every page the server hands out is disjoint from the last, so what the reader has
  // already seen stays on screen and nothing appears twice. A write reloads from the first page deliberately:
  // once somebody's roles or status changed, positions further down the list are no longer the ones read.
  const showMore = async () => {
    setIsBusy(true);
    try {
      setReadTarget('pagination');
      await roster.refresh(nextCursor, appendMembers);
    } finally {
      setIsBusy(false);
    }
  };

  const toggleRole = (roleId) => setEditing((current) => ({
    ...current,
    roleIds: current.roleIds.includes(roleId)
      ? current.roleIds.filter((held) => held !== roleId)
      : [...current.roleIds, roleId],
  }));

  /**
   * A session in no organization is a dead end, so it is composed as one screen with one thing on it: the same
   * heading, the sentence explaining the state, and the way out of it. The raised card this used to be is the
   * public entrance's treatment and this route is behind a session, so the frame is the single-object frame the
   * standard gives a form. It never renders beside the roster — the screen is this or that — which is why the
   * one `h1`, its id and its word are the same in both.
   */
  // Every refusal on this screen is scoped to the region that asked for it, which reports well only while that
  // region is on screen. A resumed operation returns to a screen mounted from scratch — no editor open, only
  // page one read — so its target can have no place to be drawn. A refusal nobody can see is worse than one
  // said plainly, so what no region claims is said for the screen instead.
  const claimedByRegion = actionTarget === `roles:${editing?.membershipId}`
    || (members ?? []).some((member) => actionTarget === `member:${member.membershipId}`);
  const unclaimedProblem = claimedByRegion ? null : actionProblem;

  if (tenantId === null) {
    return (
      <Stack component="section" aria-labelledby="members-heading" spacing={3} sx={frame}>
        <Box>
          <Typography id="members-heading" component="h1" variant="h5">{t('common:navigation.members')}</Typography>
          <Typography variant="body2" color="text.secondary" sx={supporting}>
            {t('identity:members.noOrganization')}
          </Typography>
        </Box>
        {/* This branch is a dead end: it names the missing thing and offers nothing that resolves it. The control
            belongs here and the destination exists, but its label would be a user-visible string this screen has
            never carried, so it is reported rather than written. */}
      </Stack>
    );
  }

  const nameOf = (roleId) => {
    const role = roles?.find((candidate) => candidate.roleId === roleId);
    return role ? roleName(role, t) : null;
  };
  const initialLoading = roster.status === 'loading' && roster.data === null;

  return (
    <Stack component="section" aria-labelledby="members-heading" spacing={3}>
      <Box>
        <Typography id="members-heading" component="h1" variant="h5">{t('common:navigation.members')}</Typography>
        <Typography variant="body2" color="text.secondary" sx={supporting}>
          {t('identity:members.description')}
        </Typography>
      </Box>

      {/* A control with no container is a stray field. The password is not part of any one member's form — it is
          what every sensitive action on this screen is bought with — so it gets a section of its own, at the
          width the standard gives a form rather than a width given to the field. */}
      {proof.hasPassword ? (
        <Paper variant="outlined" sx={panel}>
          <TextField
            id="members-password"
            label={t('identity:login.password')}
            type="password"
            autoComplete="current-password"
            value={password}
            onChange={(event) => setPassword(event.target.value)}
            fullWidth
          />
        </Paper>
      ) : proof.provider !== null && (
        <Typography variant="body2" color="text.secondary">
          {t('identity:members.providerProof', { provider: proof.provider })}
        </Typography>
      )}

      {/* The wait keeps the shape of what is coming, so the roster does not arrive by pushing the page down. The
          word is what a reader of the status region is told, so it is said to them and not also drawn as a line
          of content the roster then has to replace: a visible "Loading…" is not a loading state. */}
      <ProblemMessage problem={unclaimedProblem} autoFocus />

      {readTarget === 'roster' && <ProblemMessage problem={roster.problem} />}
      {readTarget === 'roster' && roster.status === 'errored' && (
        <Button
          type="button"
          variant="outlined"
          sx={selfStart}
          onClick={() => { setReadTarget(memberReadTarget.roster); roster.refresh(undefined); }}
        >
          {t('common:actions.tryAgain')}
        </Button>
      )}
      {initialLoading ? (
        <Stack spacing={1} role="status" aria-label={t('identity:members.loading')}>
          {[0, 1, 2].map((placeholder) => <Skeleton key={placeholder} variant="rounded" height={ROW_HEIGHT} />)}
        </Stack>
      ) : members === null ? null : members.length === 0 ? (
        <Paper variant="outlined" sx={empty}>
          {/* The standard asks an empty state to offer the action that creates the first item, and this screen has
              one at /members/invite. Its label is a string this screen has never rendered, so it is reported
              rather than written into a visual change. */}
          <Typography variant="body2" color="text.secondary">{t('identity:members.empty')}</Typography>
        </Paper>
      ) : (
        <Paper variant="outlined">
          <List disablePadding>
            {members.map((member, index) => (
              <ListItem key={member.membershipId} divider={index < members.length - 1} sx={memberRow}>
                <Stack spacing={1.5} useFlexGap sx={memberLayout}>
                  <Stack direction="row" spacing={2} useFlexGap sx={memberHead}>
                    <Box sx={nameBlock}>
                      <Stack direction="row" spacing={1} useFlexGap sx={nameLine}>
                        {/* `component` is not optional here: MUI maps `subtitle2` to an `h6` by default, so a
                            roster of a hundred people would put a hundred headings into the document outline
                            under this page's single `h1`. The weight is the point, the heading is not. */}
                        <Typography component="span" variant="subtitle2">{member.displayName}</Typography>
                        {/* Deliberately one element holding exactly this text, and deliberately not a chip: the
                            owner marker is read back by its whole text content. */}
                        {member.isOwner && (
                          <Typography component="span" variant="caption" color="text.secondary">{t('identity:members.owner')}</Typography>
                        )}
                      </Stack>
                      <Typography component="div" variant="caption" color="text.secondary">{member.normalizedEmail}</Typography>
                    </Box>

                    {/* What the organization has granted this person, in the order it is asked about: the state
                        first, because it decides whether the roles beside it are in force at all. */}
                    <Stack direction="row" spacing={0.5} useFlexGap sx={badges}>
                      <Chip
                        size="small"
                        variant="outlined"
                        label={t(`enums:membershipStatus.${member.status}`)}
                        color={statusColor[member.status] ?? 'default'}
                      />
                      {member.roleIds.length === 0 ? (
                        <Typography variant="caption" color="text.disabled" sx={absence}>{t('identity:members.noRoles')}</Typography>
                      ) : (
                        member.roleIds.map((roleId) => {
                          const name = nameOf(roleId);
                          return name ? <Chip key={roleId} size="small" label={name} /> : null;
                        })
                      )}
                    </Stack>
                  </Stack>

                  {actionTarget === `member:${member.membershipId}` && actionProblem && (
                    <Box sx={fullMemberRow}>
                      <ProblemMessage problem={actionProblem} autoFocus />
                    </Box>
                  )}

                  <Stack direction="row" spacing={1} useFlexGap sx={rowActions}>
                    <Button
                      type="button"
                      size="small"
                      disabled={isBusy}
                      onClick={() => {
                        setActionProblem(null);
                        setActionTarget(null);
                        setEditing({ membershipId: member.membershipId, roleIds: [...member.roleIds] });
                      }}
                    >
                      {t('identity:members.editRoles', { name: member.displayName })}
                    </Button>

                    {!member.isOwner && member.status === memberStatus.active && (
                      <Button type="button" size="small" disabled={isBusy} onClick={() => changeStatus(member, memberStatusAction.suspend)}>
                        {t('identity:members.suspend', { name: member.displayName })}
                      </Button>
                    )}
                    {!member.isOwner && member.status === memberStatus.suspended && (
                      <Button type="button" size="small" disabled={isBusy} onClick={() => changeStatus(member, memberStatusAction.reactivate)}>
                        {t('identity:members.reactivate', { name: member.displayName })}
                      </Button>
                    )}
                    {!member.isOwner && member.status !== memberStatus.revoked && (
                      <Button type="button" size="small" color="error" disabled={isBusy} onClick={() => changeStatus(member, memberStatusAction.revoke)}>
                        {t('identity:members.remove', { name: member.displayName })}
                      </Button>
                    )}
                  </Stack>

                  {/* Handing the organization over is the one change nobody can undo alone, and it is the second
                      thing on this row drawn in the error colour. Both words are what a caller reads back, so the
                      two are told apart by where they sit rather than by what they say: a rule closes the row's
                      own actions, and the transfer takes the line under it at the opposite end. Nothing here
                      leaves the member's own `li` — the editor, the actions and the address are one element,
                      because that is how a caller scopes a query to one person. */}
                  {!member.isOwner && member.status === memberStatus.active && proof.canProve && (
                    <>
                      <Divider sx={fullMemberRow} />
                      <Box sx={transferRow}>
                        <Button
                          type="button"
                          size="small"
                          color="error"
                          disabled={isBusy || !proof.canBegin(password)}
                          onClick={() => {
                            if (window.confirm(t('identity:members.transferConfirm', { name: member.displayName }))) transfer(member);
                          }}
                        >
                          {t('identity:members.transfer', { name: member.displayName })}
                        </Button>
                      </Box>
                    </>
                  )}

                  {editing?.membershipId === member.membershipId && (
                    <Stack
                      component="form"
                      spacing={2}
                      sx={editor}
                      onSubmit={(event) => { event.preventDefault(); saveRoles(member); }}
                    >
                      <Divider />
                      <ProblemMessage
                        problem={actionTarget === `roles:${member.membershipId}` ? actionProblem : null}
                        autoFocus
                      />
                      <FormControl component="fieldset">
                        <FormLabel component="legend">{t('identity:members.rolesFor', { name: member.displayName })}</FormLabel>
                        {catalog.problem?.code === 'permission_denied' && (
                          <Typography variant="body2" color="text.secondary">
                            {t('identity:members.rolesRefused')}
                          </Typography>
                        )}
                        {catalog.problem && catalog.problem.code !== 'permission_denied' && (
                          <ProblemMessage problem={catalog.problem} />
                        )}
                        {catalog.status === 'errored' && (
                          <Button
                            type="button"
                            variant="outlined"
                            sx={selfStart}
                            onClick={() => catalog.refresh(undefined)}
                          >
                            {t('common:actions.tryAgain')}
                          </Button>
                        )}
                        {roles?.length === 0 && (
                          <Typography variant="body2" color="text.secondary">{t('identity:members.noRolesToGive')}</Typography>
                        )}
                        <FormGroup>
                          {roles?.map((role) => (
                            <FormControlLabel
                              key={role.roleId}
                              htmlFor={memberRoleInputId(member.membershipId, role.roleId)}
                              control={(
                                <Checkbox
                                  id={memberRoleInputId(member.membershipId, role.roleId)}
                                  checked={editing.roleIds.includes(role.roleId)}
                                  onChange={() => toggleRole(role.roleId)}
                                />
                              )}
                              label={roleName(role, t)}
                            />
                          ))}
                        </FormGroup>
                      </FormControl>
                      <Stack direction="row" spacing={1} useFlexGap sx={row}>
                        <Button type="submit" variant="contained" disabled={isBusy || !proof.canProve || !proof.canBegin(password)}>
                          {t('identity:members.saveRoles')}
                        </Button>
                        <Button
                          type="button"
                          variant="outlined"
                          disabled={isBusy}
                          onClick={() => {
                            setActionProblem(null);
                            setActionTarget(null);
                            setEditing(null);
                          }}
                        >
                          {t('identity:members.cancel')}
                        </Button>
                      </Stack>
                    </Stack>
                  )}
                </Stack>
              </ListItem>
            ))}
          </List>
        </Paper>
      )}

      {(nextCursor !== null || (readTarget === 'pagination' && roster.problem)) && (
        <Stack spacing={1} sx={selfStart}>
          {readTarget === 'pagination' && <ProblemMessage problem={roster.problem} />}
          {readTarget === 'pagination' && roster.status === 'errored' ? (
            <Button type="button" variant="outlined" disabled={isBusy} onClick={showMore} sx={selfStart}>
              {t('common:actions.tryAgain')}
            </Button>
          ) : nextCursor !== null ? (
            <Button type="button" variant="outlined" disabled={isBusy} onClick={showMore} sx={selfStart}>
              {t('identity:members.showMore')}
            </Button>
          ) : null}
        </Stack>
      )}
    </Stack>
  );
}

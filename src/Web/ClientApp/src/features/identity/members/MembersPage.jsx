import { useCallback, useEffect, useState } from 'react';
import { useIdentity } from '../context/IdentityProvider';
import { ProblemMessage } from '../ProblemMessage';
import { useIdentityProof } from '../useIdentityProof';

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
  const proof = useIdentityProof();
  const tenantId = identity.context?.activeTenant?.id ?? null;
  const [members, setMembers] = useState(null);
  const [nextCursor, setNextCursor] = useState(null);
  const [roles, setRoles] = useState(null);
  const [password, setPassword] = useState('');
  const [problem, setProblem] = useState(null);
  const [isBusy, setIsBusy] = useState(false);
  const [editing, setEditing] = useState(null);

  const load = useCallback(async () => {
    if (tenantId === null) return;
    // The catalogue can never reject: both of its outcomes are handled where it is started, so `Promise.all`
    // fails only for the roster. Both requests still leave together, and both answers land in one continuation,
    // so the roster never renders for an instant with its role names missing.
    const catalogue = identity.client.listRoles(tenantId).then(
      (page) => page.items.filter((role) => !role.isRetired),
      () => null,
    );
    try {
      const [listed, available] = await Promise.all([identity.client.listMembers(tenantId), catalogue]);
      setMembers(listed.items);
      setNextCursor(listed.nextCursor ?? null);
      setRoles(available);
      setProblem(null);
    } catch (error) {
      setProblem(error.problem ?? { code: 'unexpected' });
    }
  }, [identity, tenantId]);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      if (!cancelled) await load();
    })();
    return () => { cancelled = true; };
  }, [load]);

  const run = async (action, act, intent = null) => {
    setIsBusy(true);
    setProblem(null);
    try {
      // A provider proof leaves for the provider rather than answering, so the change waits for the round trip.
      if (action !== null && !await proof.prove(action, password, intent)) return;
      await act();
      setPassword('');
      setEditing(null);
      await load();
    } catch (error) {
      setProblem(error.problem ?? { code: 'unexpected' });
    } finally {
      setIsBusy(false);
    }
  };

  const saveRoles = (member) => run(
    'members.roles.change',
    () => identity.client.updateMemberRoles(tenantId, member.membershipId, editing.roleIds, member.version),
    { returnTo: MembersPath, operation: 'roles', target: member.membershipId, draft: { roleIds: editing.roleIds, version: member.version } });

  const changeStatus = (member, change) =>
    run(null, () => identity.client.changeMemberStatus(tenantId, member.membershipId, change, member.version));

  const transfer = (member) => run(
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
      setProblem(null);
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
          await run(null, () => identity.client.updateMemberRoles(tenantId, member.membershipId, pending.roleIds, pending.version));
        } else if (waiting.operation === 'transfer' && !member.isOwner && member.status === 'Active') {
          await run(null, () => identity.client.transferOwnership(tenantId, member.membershipId, pending.version));
        }
      } catch (error) {
        if (!cancelled) setProblem(error.problem ?? { code: 'unexpected' });
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
    setProblem(null);
    try {
      const next = await identity.client.listMembers(tenantId, nextCursor);
      setMembers((current) => [...(current ?? []), ...next.items]);
      setNextCursor(next.nextCursor ?? null);
    } catch (error) {
      setProblem(error.problem ?? { code: 'unexpected' });
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

  if (tenantId === null) {
    return (
      <section aria-labelledby="members-heading">
        <h1 id="members-heading">Members</h1>
        <p>Choose an organization first. Members belong to one organization, and this session is not in one.</p>
      </section>
    );
  }

  const nameOf = (roleId) => roles?.find((role) => role.roleId === roleId)?.name ?? roleId;

  return (
    <section aria-labelledby="members-heading">
      <h1 id="members-heading">Members</h1>
      <ProblemMessage problem={problem} />
      <p>
        You can only give somebody a role you could have built yourself, and the organization always keeps at least
        one administrator. The owner cannot be suspended or removed &mdash; transfer the organization first.
      </p>

      {proof.hasPassword ? (
        <>
          <label htmlFor="members-password">Password</label>
          <input
            id="members-password"
            type="password"
            autoComplete="current-password"
            value={password}
            onChange={(event) => setPassword(event.target.value)}
          />
        </>
      ) : proof.provider !== null && (
        <p>You have no password here. Changing roles or handing the organization over asks {proof.provider} to confirm it is you.</p>
      )}

      {members === null || !proof.isReady ? <p role="status">Loading&hellip;</p> : (
        <ul>
          {members.map((member) => (
            <li key={member.membershipId}>
              <span>{member.displayName}</span>
              <span> &middot; {member.normalizedEmail}</span>
              <span> &middot; {member.status}</span>
              {member.isOwner && <span> &mdash; owner</span>}
              <span> &middot; {member.roleIds.length === 0 ? 'no roles' : member.roleIds.map(nameOf).join(', ')}</span>

              <button
                type="button"
                disabled={isBusy}
                onClick={() => setEditing({ membershipId: member.membershipId, roleIds: [...member.roleIds] })}
              >
                Edit roles of {member.displayName}
              </button>

              {!member.isOwner && member.status === 'Active' && (
                <button type="button" disabled={isBusy} onClick={() => changeStatus(member, 'suspend')}>
                  Suspend {member.displayName}
                </button>
              )}
              {!member.isOwner && member.status === 'Suspended' && (
                <button type="button" disabled={isBusy} onClick={() => changeStatus(member, 'reactivate')}>
                  Reactivate {member.displayName}
                </button>
              )}
              {!member.isOwner && member.status !== 'Revoked' && (
                <button type="button" disabled={isBusy} onClick={() => changeStatus(member, 'revoke')}>
                  Remove {member.displayName}
                </button>
              )}

              {/* Handing the organization over is the one change nobody can undo alone, so it is asked for
                  explicitly rather than offered as one more button among the rest. */}
              {!member.isOwner && member.status === 'Active' && proof.canProve && (
                <button
                  type="button"
                  disabled={isBusy || !proof.canBegin(password)}
                  onClick={() => {
                    if (window.confirm(`Give this organization to ${member.displayName}? You will stop being its owner.`)) transfer(member);
                  }}
                >
                  Transfer ownership to {member.displayName}
                </button>
              )}

              {editing?.membershipId === member.membershipId && (
                <form onSubmit={(event) => { event.preventDefault(); saveRoles(member); }}>
                  <fieldset>
                    <legend>Roles for {member.displayName}</legend>
                    {roles === null && <p>You cannot see this organization&rsquo;s roles, so there are none to give here.</p>}
                    {roles?.length === 0 && <p>This organization has no roles to give yet.</p>}
                    {roles?.map((role) => (
                      <label key={role.roleId} htmlFor={`role-${member.membershipId}-${role.roleId}`}>
                        <input
                          id={`role-${member.membershipId}-${role.roleId}`}
                          type="checkbox"
                          checked={editing.roleIds.includes(role.roleId)}
                          onChange={() => toggleRole(role.roleId)}
                        />
                        {role.name}
                      </label>
                    ))}
                  </fieldset>
                  <button type="submit" disabled={isBusy || !proof.canProve || !proof.canBegin(password)}>Save roles</button>
                  <button type="button" disabled={isBusy} onClick={() => setEditing(null)}>Cancel</button>
                </form>
              )}
            </li>
          ))}
        </ul>
      )}

      {nextCursor !== null && (
        <button type="button" disabled={isBusy} onClick={showMore}>Show more members</button>
      )}
    </section>
  );
}

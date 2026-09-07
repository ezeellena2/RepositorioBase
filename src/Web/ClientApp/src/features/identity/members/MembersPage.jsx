import { useCallback, useEffect, useState } from 'react';
import { useIdentity } from '../context/IdentityProvider';
import { ProblemMessage } from '../ProblemMessage';

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
 * and revoking echo the row's own version instead. The password field is cleared the moment it is used and is
 * never written anywhere (IA-REQ-025, IA-REQ-051).
 */
export function MembersPage() {
  const identity = useIdentity();
  const tenantId = identity.context?.activeTenant?.id ?? null;
  const [members, setMembers] = useState(null);
  const [roles, setRoles] = useState([]);
  const [password, setPassword] = useState('');
  const [problem, setProblem] = useState(null);
  const [isBusy, setIsBusy] = useState(false);
  const [editing, setEditing] = useState(null);

  const load = useCallback(async () => {
    if (tenantId === null) return;
    try {
      const [listed, available] = await Promise.all([
        identity.client.listMembers(tenantId),
        identity.client.listRoles(tenantId),
      ]);
      setMembers(listed.items);
      setRoles(available.items.filter((role) => !role.isRetired));
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

  const run = async (act) => {
    setIsBusy(true);
    setProblem(null);
    try {
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

  const saveRoles = (member) => run(async () => {
    await identity.client.reauthenticate('members.roles.change', password);
    await identity.client.updateMemberRoles(tenantId, member.membershipId, editing.roleIds, member.version);
  });

  const changeStatus = (member, change) =>
    run(() => identity.client.changeMemberStatus(tenantId, member.membershipId, change, member.version));

  const transfer = (member) => run(async () => {
    await identity.client.reauthenticate('tenant.ownership.transfer', password);
    await identity.client.transferOwnership(tenantId, member.membershipId, member.version);
  });

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

  const nameOf = (roleId) => roles.find((role) => role.roleId === roleId)?.name ?? roleId;

  return (
    <section aria-labelledby="members-heading">
      <h1 id="members-heading">Members</h1>
      <ProblemMessage problem={problem} />
      <p>
        You can only give somebody a role you could have built yourself, and the organization always keeps at least
        one administrator. The owner cannot be suspended or removed &mdash; transfer the organization first.
      </p>

      <label htmlFor="members-password">Password</label>
      <input
        id="members-password"
        type="password"
        autoComplete="current-password"
        value={password}
        onChange={(event) => setPassword(event.target.value)}
      />

      {members === null ? <p role="status">Loading&hellip;</p> : (
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
              {!member.isOwner && member.status === 'Active' && (
                <button
                  type="button"
                  disabled={isBusy || password.length === 0}
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
                    {roles.length === 0 && <p>This organization has no roles to give yet.</p>}
                    {roles.map((role) => (
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
                  <button type="submit" disabled={isBusy || password.length === 0}>Save roles</button>
                  <button type="button" disabled={isBusy} onClick={() => setEditing(null)}>Cancel</button>
                </form>
              )}
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}

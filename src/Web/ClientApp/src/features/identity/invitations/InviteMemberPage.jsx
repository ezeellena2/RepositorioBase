import { useCallback, useEffect, useState } from 'react';
import { useIdentity } from '../context/IdentityProvider';
import { ProblemMessage } from '../ProblemMessage';

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
  const identity = useIdentity();
  const tenantId = identity.context?.activeTenant?.id ?? null;
  const [roles, setRoles] = useState(null);
  const [invitations, setInvitations] = useState(null);
  const [nextCursor, setNextCursor] = useState(null);
  const [roleIds, setRoleIds] = useState([]);
  const [email, setEmail] = useState('');
  const [problem, setProblem] = useState(null);
  const [sent, setSent] = useState(null);
  const [isBusy, setIsBusy] = useState(false);

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

  const run = async (act) => {
    setIsBusy(true);
    setProblem(null);
    try {
      const value = await act();
      await load();
      return value;
    } catch (error) {
      setProblem(error.problem ?? { code: 'unexpected' });
      return undefined;
    } finally {
      setIsBusy(false);
    }
  };

  const invite = async (event) => {
    event.preventDefault();
    setSent(null);
    const issued = await run(() => identity.client.inviteMember(tenantId, email, roleIds));
    if (issued) {
      setSent(issued);
      setEmail('');
      setRoleIds([]);
    }
  };

  // A continuation appends, so every offer the reader has seen stays on screen. Each page the server hands out
  // is disjoint from the last, so an offer cannot be listed twice.
  const showMore = async () => {
    setIsBusy(true);
    setProblem(null);
    try {
      const next = await identity.client.listTenantInvitations(tenantId, nextCursor);
      setInvitations((current) => [...(current ?? []), ...next.items]);
      setNextCursor(next.nextCursor ?? null);
    } catch (error) {
      setProblem(error.problem ?? { code: 'unexpected' });
    } finally {
      setIsBusy(false);
    }
  };

  const toggleRole = (roleId) => setRoleIds((current) =>
    current.includes(roleId) ? current.filter((held) => held !== roleId) : [...current, roleId]);

  if (tenantId === null) {
    return (
      <section aria-labelledby="invite-heading">
        <h1 id="invite-heading">Invite a member</h1>
        <p>Choose an organization first. An invitation belongs to one organization, and this session is not in one.</p>
      </section>
    );
  }

  const nameOf = (roleId) => roles?.find((role) => role.roleId === roleId)?.name ?? roleId;

  return (
    <section aria-labelledby="invite-heading">
      <h1 id="invite-heading">Invite a member</h1>
      <ProblemMessage problem={problem} />
      {sent && <p role="status">Invitation sent. It expires on {new Date(sent.expiresAt).toLocaleString()}.</p>}

      <form onSubmit={invite}>
        <label htmlFor="invite-email">Email</label>
        <input id="invite-email" type="email" value={email} onChange={(event) => setEmail(event.target.value)} required />

        <fieldset>
          <legend>Roles to offer</legend>
          {roles === null && <p>You cannot see this organization&rsquo;s roles, so there are none to offer here.</p>}
          {roles?.length === 0 && <p>This organization has no roles to offer yet.</p>}
          {roles?.map((role) => (
            <label key={role.roleId} htmlFor={`invite-role-${role.roleId}`}>
              <input
                id={`invite-role-${role.roleId}`}
                type="checkbox"
                checked={roleIds.includes(role.roleId)}
                onChange={() => toggleRole(role.roleId)}
              />
              {role.name}
            </label>
          ))}
        </fieldset>

        <button type="submit" disabled={isBusy}>Send invitation</button>
      </form>

      <h2>Invitations</h2>
      {invitations === null ? <p>You cannot see this organization&rsquo;s invitations.</p> : (
        <ul>
          {invitations.length === 0 && <li>No invitation has been sent yet.</li>}
          {invitations.map((invitation) => (
            <li key={invitation.invitationId}>
              <span>{invitation.normalizedEmail}</span>
              <span> &middot; {invitation.status}</span>
              <span> &middot; expires {new Date(invitation.expiresAt).toLocaleString()}</span>
              <span> &middot; {invitation.roleIds.length === 0 ? 'no roles' : invitation.roleIds.map(nameOf).join(', ')}</span>

              {/* Only a standing offer can be reissued or withdrawn. One already accepted or already withdrawn
                  is shown because it happened, not because there is anything left to do to it. */}
              {invitation.status === 'Pending' && (
                <>
                  <button
                    type="button"
                    disabled={isBusy}
                    onClick={() => run(() => identity.client.resendInvitation(tenantId, invitation.invitationId))}
                  >
                    Resend to {invitation.normalizedEmail}
                  </button>
                  <button
                    type="button"
                    disabled={isBusy}
                    onClick={() => {
                      if (window.confirm(`Withdraw the invitation to ${invitation.normalizedEmail}? Their link stops working.`)) {
                        run(() => identity.client.cancelInvitation(tenantId, invitation.invitationId));
                      }
                    }}
                  >
                    Withdraw invitation to {invitation.normalizedEmail}
                  </button>
                </>
              )}
            </li>
          ))}
        </ul>
      )}

      {nextCursor !== null && (
        <button type="button" disabled={isBusy} onClick={showMore}>Show more invitations</button>
      )}
    </section>
  );
}

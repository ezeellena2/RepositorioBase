import { useState } from 'react';
import { useIdentity } from '../context/IdentityProvider';
import { ProblemMessage } from '../ProblemMessage';
import { useSubmit } from '../useSubmit';

/**
 * The response carries the invitation's identifier and expiry and never its token: the usable credential is
 * minted for the recipient and reaches them by email, so there is nothing here to show or copy
 * (IA-REQ-015/018).
 */
export function InviteMemberPage() {
  const identity = useIdentity();
  const [email, setEmail] = useState('');
  const [roleIds, setRoleIds] = useState('');
  const tenantId = identity?.context?.activeTenant?.id;
  const { submit, problem, isBusy, result } = useSubmit((address, roles) =>
    identity.client.inviteMember(tenantId, address, roles));

  return (
    <section aria-labelledby="invite-heading">
      <h1 id="invite-heading">Invite a member</h1>
      <ProblemMessage problem={problem} />
      {result && <p role="status">Invitation sent. It expires on {new Date(result.expiresAt).toLocaleString()}.</p>}
      <form onSubmit={(event) => {
        event.preventDefault();
        submit(email, roleIds.split(',').map((value) => value.trim()).filter(Boolean));
      }}>
        <label htmlFor="invite-email">Email</label>
        <input id="invite-email" type="email" value={email} onChange={(event) => setEmail(event.target.value)} required />
        <label htmlFor="invite-roles">Role identifiers</label>
        <input id="invite-roles" value={roleIds} onChange={(event) => setRoleIds(event.target.value)} required />
        <button type="submit" disabled={isBusy || !tenantId}>Send invitation</button>
      </form>
    </section>
  );
}

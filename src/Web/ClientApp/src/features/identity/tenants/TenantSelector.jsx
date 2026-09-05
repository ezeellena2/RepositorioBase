import { useIdentity } from '../context/IdentityProvider';
import { ProblemMessage } from '../ProblemMessage';
import { useSubmit } from '../useSubmit';

/**
 * The active tenant is never derived from client state: selecting one is a request, and what comes back is the
 * context the server decided on (SPEC section 7). Selection does not change the authentication state, so the
 * antiforgery pair is kept rather than bootstrapped again.
 */
export function TenantSelector() {
  const identity = useIdentity();
  const { submit, problem, isBusy } = useSubmit((tenantId) => identity.selectTenant(tenantId));
  const tenants = identity?.context?.availableTenants ?? [];
  const activeId = identity?.context?.activeTenant?.id;

  return (
    <section aria-labelledby="tenants-heading">
      <h1 id="tenants-heading">Choose an organization</h1>
      <ProblemMessage problem={problem} />
      {tenants.length === 0 ? <p>You do not belong to an organization yet.</p> : (
        <ul>
          {tenants.map((tenant) => (
            <li key={tenant.id}>
              <button type="button" disabled={isBusy || tenant.id === activeId} onClick={() => submit(tenant.id)}>
                {tenant.name}{tenant.id === activeId ? ' (current)' : ''}
              </button>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}

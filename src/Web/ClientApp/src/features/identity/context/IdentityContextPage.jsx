import { useIdentity } from './IdentityProvider';

/**
 * What the session actually grants, as the server reports it. The permissions are shown for orientation only —
 * every operation is authorized again server-side, so this page never gates anything by itself.
 */
export function IdentityContextPage() {
  const identity = useIdentity();
  const context = identity?.context;

  return (
    <section aria-labelledby="identity-heading">
      <h1 id="identity-heading">Your access</h1>
      <dl>
        <dt>Signed in as</dt>
        <dd>{context?.user?.displayName ?? 'unknown'}</dd>
        <dt>Active organization</dt>
        <dd>{context?.activeTenant?.name ?? 'none selected'}</dd>
        <dt>Permissions</dt>
        <dd>
          {(context?.permissions ?? []).length === 0
            ? 'none in this organization'
            : <ul>{context.permissions.map((permission) => <li key={permission}>{permission}</li>)}</ul>}
        </dd>
      </dl>
    </section>
  );
}

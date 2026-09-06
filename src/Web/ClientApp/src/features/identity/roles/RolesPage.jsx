import { useCallback, useEffect, useState } from 'react';
import { useIdentity } from '../context/IdentityProvider';
import { ProblemMessage } from '../ProblemMessage';

const EMPTY_DRAFT = { roleId: null, name: '', permissions: [], version: null };

/**
 * Custom roles inside the organization the session is operating in (IA-REQ-053).
 *
 * Two rules shape this screen and both belong to the server. The **ceiling** — you can only grant what you hold —
 * is why the catalogue is asked for rather than assumed: a code this administrator cannot grant is not offered,
 * so nobody composes a role that will be refused without being told which code was the problem. The **floor** —
 * an organization always keeps an administrator — is why a refusal here is shown rather than worked around: only
 * the server can count, and it counts after the change it is about to reject.
 *
 * Every write buys a proof first (amendment D2). The password is typed into a field that is cleared the moment it
 * is used and is never written anywhere (IA-REQ-025, IA-REQ-051).
 */
export function RolesPage() {
  const identity = useIdentity();
  const tenantId = identity.context?.activeTenant?.id ?? null;
  const [roles, setRoles] = useState(null);
  const [catalog, setCatalog] = useState([]);
  const [draft, setDraft] = useState(EMPTY_DRAFT);
  const [password, setPassword] = useState('');
  const [problem, setProblem] = useState(null);
  const [isBusy, setIsBusy] = useState(false);

  const load = useCallback(async () => {
    if (tenantId === null) return;
    try {
      const [listed, entries] = await Promise.all([
        identity.client.listRoles(tenantId),
        identity.client.listPermissionCatalog(tenantId),
      ]);
      setRoles(listed.items);
      setCatalog(entries);
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

  const run = async (action, act) => {
    setIsBusy(true);
    setProblem(null);
    try {
      // The proof is bought immediately before the change and spent by it. It is single-use, so each change
      // asks again — which is what "recent" has to mean to be worth anything.
      await identity.client.reauthenticate(action, password);
      await act();
      setPassword('');
      setDraft(EMPTY_DRAFT);
      await load();
    } catch (error) {
      setProblem(error.problem ?? { code: 'unexpected' });
    } finally {
      setIsBusy(false);
    }
  };

  const save = () => run('roles.change', () => (draft.roleId === null
    ? identity.client.createRole(tenantId, draft.name, draft.permissions)
    : identity.client.updateRole(tenantId, draft.roleId, draft.name, draft.permissions, draft.version)));

  const retire = (role) => run('roles.change', () => identity.client.retireRole(tenantId, role.roleId, role.version));

  const toggle = (code) => setDraft((current) => ({
    ...current,
    permissions: current.permissions.includes(code)
      ? current.permissions.filter((held) => held !== code)
      : [...current.permissions, code],
  }));

  if (tenantId === null) {
    return (
      <section aria-labelledby="roles-heading">
        <h1 id="roles-heading">Roles</h1>
        <p>Choose an organization first. Roles belong to one organization, and this session is not in one.</p>
      </section>
    );
  }

  const grantable = catalog.filter((entry) => entry.grantable);

  return (
    <section aria-labelledby="roles-heading">
      <h1 id="roles-heading">Roles</h1>
      <ProblemMessage problem={problem} />
      <p>
        A role is a label with permissions behind it. You can only put permissions into a role that you hold
        yourself, and the organization always keeps at least one administrator.
      </p>

      <label htmlFor="roles-password">Password</label>
      <input
        id="roles-password"
        type="password"
        autoComplete="current-password"
        value={password}
        onChange={(event) => setPassword(event.target.value)}
      />

      {roles === null ? <p role="status">Loading…</p> : (
        <ul>
          {roles.map((role) => (
            <li key={role.roleId}>
              <span>{role.name}</span>
              {role.isSystem && <span> — built in</span>}
              {role.isRetired && <span> — retired</span>}
              <span> · {role.permissions.length === 0 ? 'no permissions' : role.permissions.join(', ')}</span>
              {!role.isSystem && !role.isRetired && (
                <>
                  <button
                    type="button"
                    disabled={isBusy}
                    onClick={() => setDraft({ roleId: role.roleId, name: role.name, permissions: [...role.permissions], version: role.version })}
                  >
                    Edit {role.name}
                  </button>
                  <button type="button" disabled={isBusy || password.length === 0} onClick={() => retire(role)}>
                    Retire {role.name}
                  </button>
                </>
              )}
            </li>
          ))}
        </ul>
      )}

      <h2>{draft.roleId === null ? 'New role' : `Editing ${draft.name}`}</h2>
      <form onSubmit={(event) => { event.preventDefault(); save(); }}>
        <label htmlFor="role-name">Name</label>
        <input id="role-name" value={draft.name} onChange={(event) => setDraft({ ...draft, name: event.target.value })} required />

        <fieldset>
          <legend>Permissions you can grant</legend>
          {grantable.length === 0 && <p>You hold no permissions that can be put into a role.</p>}
          {grantable.map((entry) => (
            <label key={entry.code} htmlFor={`permission-${entry.code}`}>
              <input
                id={`permission-${entry.code}`}
                type="checkbox"
                checked={draft.permissions.includes(entry.code)}
                onChange={() => toggle(entry.code)}
              />
              {entry.code}
            </label>
          ))}
        </fieldset>

        <button type="submit" disabled={isBusy || password.length === 0}>
          {draft.roleId === null ? 'Create role' : 'Save role'}
        </button>
        {draft.roleId !== null && (
          <button type="button" disabled={isBusy} onClick={() => setDraft(EMPTY_DRAFT)}>Cancel</button>
        )}
      </form>
    </section>
  );
}

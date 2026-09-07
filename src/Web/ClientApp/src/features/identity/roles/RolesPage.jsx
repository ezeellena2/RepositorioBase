import { useCallback, useEffect, useState } from 'react';
import { useIdentity } from '../context/IdentityProvider';
import { ProblemMessage } from '../ProblemMessage';
import { useIdentityProof } from '../useIdentityProof';

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
 * Every write buys a proof first (amendment D2), and which proof depends on what this identity has: a password
 * typed into a field that is cleared the moment it is used and is never written anywhere, or a round trip to the
 * provider it signed in with (IA-REQ-025, IA-REQ-051).
 *
 * The list is shown a page at a time, because an organization can hold more roles than one page carries and a
 * screen that silently stops at a hundred is a screen that lies about what the organization has.
 */
const RolesPath = '/roles';

export function RolesPage() {
  const identity = useIdentity();
  const proof = useIdentityProof();
  const tenantId = identity.context?.activeTenant?.id ?? null;
  const [roles, setRoles] = useState(null);
  const [nextCursor, setNextCursor] = useState(null);
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
      setNextCursor(listed.nextCursor ?? null);
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

  const run = async (action, act, intent = null) => {
    setIsBusy(true);
    setProblem(null);
    try {
      // The proof is bought immediately before the change and spent by it. It is single-use, so each change
      // asks again — which is what "recent" has to mean to be worth anything. A provider proof leaves for the
      // provider rather than answering, so the change waits for the round trip instead of being sent now.
      if (action !== null && !await proof.prove(action, password, intent)) return;
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

  const save = () => run(
    'roles.change',
    () => (draft.roleId === null
      ? identity.client.createRole(tenantId, draft.name, draft.permissions)
      : identity.client.updateRole(tenantId, draft.roleId, draft.name, draft.permissions, draft.version)),
    { returnTo: RolesPath, operation: draft.roleId === null ? 'create' : 'update', draft });

  const retire = (role) => run(
    'roles.change',
    () => identity.client.retireRole(tenantId, role.roleId, role.version),
    { returnTo: RolesPath, operation: 'retire', draft: { roleId: role.roleId, version: role.version } });

  // The edit this screen left behind, resumed once the server accepted the provider round trip. The draft is
  // replayed with the version the person actually read, so a role somebody else changed in the meantime is
  // refused by the server exactly as it would have been without the detour.
  const waiting = proof.resumable(RolesPath);
  useEffect(() => {
    if (waiting === null || roles === null || !proof.isReady) return;
    let cancelled = false;
    void Promise.resolve().then(async () => {
      if (cancelled) return;
      setIsBusy(true);
      setProblem(null);
      try {
        const pending = waiting.draft ?? {};
        if (waiting.operation === 'create') {
          proof.forget();
          await run(null, () => identity.client.createRole(tenantId, pending.name, pending.permissions));
          return;
        }

        // The role may have been selected on a later page before leaving for the provider.
        let role = roles.find((candidate) => candidate.roleId === pending.roleId);
        let cursor = nextCursor;
        while (role === undefined && cursor !== null) {
          const page = await identity.client.listRoles(tenantId, cursor);
          if (cancelled) return;
          role = page.items.find((candidate) => candidate.roleId === pending.roleId);
          cursor = page.nextCursor ?? null;
        }
        if (cancelled || proof.resumable(RolesPath) !== waiting) return;
        proof.forget();
        if (role === undefined || role.isSystem || role.isRetired) return;
        if (waiting.operation === 'retire') {
          await run(null, () => identity.client.retireRole(tenantId, pending.roleId, pending.version));
        } else if (waiting.operation === 'update') {
          await run(null, () => identity.client.updateRole(tenantId, pending.roleId, pending.name, pending.permissions, pending.version));
        }
      } catch (error) {
        if (!cancelled) setProblem(error.problem ?? { code: 'unexpected' });
      } finally {
        if (!cancelled) setIsBusy(false);
      }
    });
    return () => { cancelled = true; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [waiting, roles, nextCursor, proof.isReady, tenantId]);

  // A continuation appends rather than replaces: what the reader has already seen stays on screen, and every
  // page the server hands out is disjoint from the last, so nothing can appear twice.
  const showMore = async () => {
    setIsBusy(true);
    setProblem(null);
    try {
      const next = await identity.client.listRoles(tenantId, nextCursor);
      setRoles((current) => [...(current ?? []), ...next.items]);
      setNextCursor(next.nextCursor ?? null);
    } catch (error) {
      setProblem(error.problem ?? { code: 'unexpected' });
    } finally {
      setIsBusy(false);
    }
  };

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

      {proof.hasPassword ? (
        <>
          <label htmlFor="roles-password">Password</label>
          <input
            id="roles-password"
            type="password"
            autoComplete="current-password"
            value={password}
            onChange={(event) => setPassword(event.target.value)}
          />
        </>
      ) : proof.provider !== null && (
        <p>You have no password here. Every change asks {proof.provider} to confirm it is you.</p>
      )}

      {roles === null || !proof.isReady ? <p role="status">Loading…</p> : (
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
                  {proof.canProve && (
                    <button type="button" disabled={isBusy || !proof.canBegin(password)} onClick={() => retire(role)}>
                      Retire {role.name}
                    </button>
                  )}
                </>
              )}
            </li>
          ))}
        </ul>
      )}

      {nextCursor !== null && (
        <button type="button" disabled={isBusy} onClick={showMore}>Show more roles</button>
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

        <button type="submit" disabled={isBusy || !proof.canProve || !proof.canBegin(password)}>
          {draft.roleId === null ? 'Create role' : 'Save role'}
        </button>
        {draft.roleId !== null && (
          <button type="button" disabled={isBusy} onClick={() => setDraft(EMPTY_DRAFT)}>Cancel</button>
        )}
      </form>
    </section>
  );
}

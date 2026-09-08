import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import App from '../../../App';
import { server } from '../../../test/server';
import { antiforgery, problem, signedInContext } from '../../../test/identityServer';

const tenant = { id: 'added-tenant', type: 'Personal', name: 'New context' };
const profile = { fullName: 'Ana Perez', displayName: 'Ana', email: 'ana@example.test', personalTenantId: tenant.id, document: null, version: '1', updatedAt: '2026-09-08T12:00:00Z' };

async function begin(kind, failRefresh) {
  let mutations = 0;
  let reads = 0;
  const base = signedInContext({ activeTenant: null, availableTenants: [] });
  const added = { ...base, availableTenants: [tenant] };
  server.use(antiforgery(), http.get('/api/identity/context', () => {
    reads += 1;
    return mutations && failRefresh ? problem(503, 'context_unreadable') : HttpResponse.json(mutations ? added : base);
  }), http.get('/api/identity/profile', () => mutations ? HttpResponse.json(profile) : problem(404, 'personal_profile_not_found')),
  http.post('/api/invitations/accept', () => { mutations += 1; return HttpResponse.json({ tenantId: tenant.id, membershipId: 'membership-1' }); }),
  http.post('/api/identity/personal', () => { mutations += 1; return new HttpResponse(null, { status: 204 }); }),
  http.put('/api/identity/context/tenant', () => HttpResponse.json({ ...added, activeTenant: tenant })));
  const path = kind === 'invitation' ? '/invitations/accept' : '/identity/profile';
  window.history.replaceState({}, '', `${path}${kind === 'invitation' ? '#token=invitation-1' : ''}`);
  render(<MemoryRouter initialEntries={[path]}><App /></MemoryRouter>);
  if (kind === 'personal') {
    await userEvent.type(await screen.findByLabelText('Full name'), 'Ana Perez');
    await userEvent.type(screen.getByLabelText('Display name'), 'Ana');
    await userEvent.type(screen.getByLabelText('DNI'), '12345678');
  }
  const button = await screen.findByRole('button', { name: kind === 'invitation' ? 'Accept' : 'Add my personal account' });
  await waitFor(() => expect(button).toBeEnabled());
  await userEvent.click(button);
  return { mutations: () => mutations, reads: () => reads };
}

describe('shared context after a membership is created', () => {
  it.each(['invitation', 'personal'])('offers and selects the new %s context through SPA navigation without remounting', async (kind) => {
    const calls = await begin(kind, false);
    await waitFor(() => expect(calls.reads()).toBe(2));
    await userEvent.click(screen.getByRole('link', { name: 'Organizations', exact: true }));
    await userEvent.click(await screen.findByRole('button', { name: 'New context' }));
    await screen.findByRole('button', { name: 'New context (current)' });
    expect(calls.mutations()).toBe(1);
  });

  it.each(['invitation', 'personal'])('reports a failed refresh after successful %s creation without offering to repeat the mutation', async (kind) => {
    const calls = await begin(kind, true);
    await waitFor(() => expect(calls.reads()).toBe(2));
    await screen.findByRole('alert');
    expect(screen.queryByRole('button', { name: kind === 'invitation' ? 'Accept' : 'Add my personal account' })).not.toBeInTheDocument();
    expect(calls.mutations()).toBe(1);
  });
});

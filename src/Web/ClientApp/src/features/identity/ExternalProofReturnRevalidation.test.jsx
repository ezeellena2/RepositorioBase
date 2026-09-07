import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, expect, it, vi } from 'vitest';
import App from '../../App';
import { server } from '../../test/server';
import { antiforgery, contextIs, problem, signedInContext } from '../../test/identityServer';
import { externalNavigation } from './externalNavigation';

const CURRENT = 'AAAAAAAAAAAAAAAAAAAAAA';
const TARGET = 'BBBBBBBBBBBBBBBBBBBBBB';
const UNRELATED = 'CCCCCCCCCCCCCCCCCCCCCC';
const renderAt = (path) => render(<MemoryRouter initialEntries={[path]}><App /></MemoryRouter>);

afterEach(() => vi.restoreAllMocks());

// The API fixture grants a valid action-bound proof on completion. It intentionally assumes the provider
// succeeded so this test isolates the application's return journey, without claiming to exercise Google or
// the backend proof implementation. The full App unmount models losing React state during provider navigation.
it('R6A resumes the original device revocation exactly once after a successful provider return', async () => {
  const starts = [];
  const revocations = [];
  let completions = 0;
  let proofAvailable = false;
  let liveSessions = [
    [CURRENT, true, 'Windows'],
    [TARGET, false, 'Android'],
    [UNRELATED, false, 'Linux'],
  ].map(([sessionRef, isCurrent, deviceLabel]) => ({
    sessionRef, isCurrent, deviceLabel, createdAt: '2026-09-07T10:00:00Z',
    lastSeenAt: '2026-09-07T10:00:00Z', expiresAt: '2026-09-07T22:00:00Z',
  }));
  const leave = vi.spyOn(externalNavigation, 'leaveFor').mockImplementation(() => {});

  server.use(
    antiforgery(),
    contextIs(signedInContext({ permissions: ['identity.sessions.manage', 'identity.credentials.manage', 'identity.external.manage'] })),
    http.get('/api/identity/credentials', () => HttpResponse.json({ hasPassword: false, passwordUpdatedAt: null })),
    http.get('/api/identity/external', () => HttpResponse.json({
      available: ['Google'],
      items: [{ handle: 'linked-google', provider: 'Google', providerEmail: 'owner@provider.test', linkedAt: '2026-09-01T00:00:00Z' }],
    })),
    http.get('/api/identity/sessions', () => HttpResponse.json(liveSessions)),
    http.post('/api/identity/external/Google/proof/start', async ({ request }) => {
      starts.push(await request.json());
      return HttpResponse.json({ authorizationRequestUri: '/api/identity/external/Google/challenge' });
    }),
    http.post('/api/identity/external/complete', () => {
      completions += 1;
      if (completions !== 1 || starts[0]?.action !== 'sessions.revoke-one') return problem(400, 'invalid_external_login');
      proofAvailable = true;
      return new HttpResponse(null, { status: 204 });
    }),
    http.delete('/api/identity/sessions/:sessionRef', ({ params }) => {
      revocations.push(params.sessionRef);
      if (!proofAvailable) return problem(401, 'recent_proof_required');
      proofAvailable = false;
      liveSessions = liveSessions.filter((session) => session.sessionRef !== params.sessionRef);
      return new HttpResponse(null, { status: 204 });
    }),
  );

  const originalPage = renderAt('/identity/sessions');
  const targetRow = (await screen.findByText('Android')).closest('li');
  await userEvent.click(within(targetRow).getByRole('button', { name: 'End this device' }));
  await waitFor(() => expect(leave).toHaveBeenCalledOnce());
  expect(starts).toEqual([{ action: 'sessions.revoke-one' }]);
  expect(revocations).toEqual([]);

  originalPage.unmount();
  renderAt('/external/return?outcome=proved');
  await waitFor(() => expect(completions).toBe(1));

  await waitFor(() => expect({
    heading: screen.queryByRole('heading', { level: 1 })?.textContent,
    completions,
    proofStarts: starts.length,
    revocations,
    proofAvailable,
    remainingDevices: liveSessions.map((session) => session.deviceLabel),
  }).toEqual({
    heading: 'Your devices',
    completions: 1,
    proofStarts: 1,
    revocations: [TARGET],
    proofAvailable: false,
    remainingDevices: ['Windows', 'Linux'],
  }));
  expect(screen.queryByText('Android')).not.toBeInTheDocument();
  expect(screen.getByText('Windows')).toBeInTheDocument();
  expect(screen.getByText('Linux')).toBeInTheDocument();
});

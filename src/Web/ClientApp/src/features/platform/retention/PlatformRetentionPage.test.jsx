import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { delay, http, HttpResponse } from 'msw';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, describe, expect, it } from 'vitest';
import { IdentityProvider } from '../../identity/context/IdentityProvider';
import { PlatformRetentionPage } from './PlatformRetentionPage';
import { server } from '../../../test/server';
import { antiforgery, contextIs, problem, signedInContext } from '../../../test/identityServer';

const RETENTION_PERMISSIONS = ['platform.retention.read', 'platform.retention.manage'];

const retentionContext = (permissions = RETENTION_PERMISSIONS, session = {}) => signedInContext({
  activeTenant: { id: 'platform-1', type: 'Platform', name: 'platform' },
  availableTenants: [{ id: 'platform-1', type: 'Platform', name: 'platform' }],
  permissions,
  session: { expiresAt: '2026-12-31T00:00:00Z', requiresTwoFactor: false, ...session },
});

/** Exactly the members `RetentionPolicyView` declares, for a deployment that has a policy. */
const POLICY = {
  policyId: 'retention-2026-01',
  version: '3',
  owner: 'Data Protection Office',
  approvedOn: '2026-01-15',
  source: 'docs/retention-policy.md',
  personalDataMode: 'Synthetic',
  activeHoldCount: 2,
  categories: [
    { category: 'AuditEvents', retentionPeriod: 'P2Y', trigger: 'RecordCreation', action: 'Erase', evidenceRequired: true },
    { category: 'SessionRecords', retentionPeriod: 'P30D', trigger: 'LastActivity', action: 'Erase', evidenceRequired: false },
  ],
};

/** The same view for a deployment with no configured policy: every optional member null, no categories. */
const NO_POLICY = {
  policyId: null,
  version: null,
  owner: null,
  approvedOn: null,
  source: null,
  personalDataMode: 'Real',
  activeHoldCount: 4,
  categories: [],
};

/** Exactly the members `placeRetentionHold` declares it will read back. */
const HOLD = {
  holdId: '6f5f1a6c-6f0e-4c0a-9a1e-6f0e4c0a9a1e',
  subjectIdentityId: '11111111-2222-3333-4444-555555555555',
  reasonCode: 'Litigation:2026-114',
  reference: 'CASE-2026-114',
  placedAt: '2026-09-08T10:15:00Z',
  placedByMembershipId: '99999999-8888-7777-6666-555555555555',
  version: 1,
};

const policyIs = (body = POLICY) => http.get('/api/platform/retention/policy', () => HttpResponse.json(body));

const deferred = () => {
  let resolve;
  const promise = new Promise((onResolve) => { resolve = onResolve; });
  return { promise, resolve };
};

const renderPage = () => render(
  <MemoryRouter><IdentityProvider><PlatformRetentionPage /></IdentityProvider></MemoryRouter>,
);

/** Every path the browser asked for, so a test can prove a request was never made rather than only unobserved. */
const trackRequests = () => {
  const paths = [];
  server.events.on('request:start', ({ request }) => paths.push(new URL(request.url).pathname));
  return paths;
};

const retentionCalls = (paths) => paths.filter((path) => path.startsWith('/api/platform/retention'));

const placeAHold = async ({ subject = HOLD.subjectIdentityId, reasonCode = HOLD.reasonCode, reference = HOLD.reference } = {}) => {
  await userEvent.type(await screen.findByLabelText('Subject identity'), subject);
  await userEvent.type(screen.getByLabelText('Reason code'), reasonCode);
  await userEvent.type(screen.getByLabelText('Reference'), reference);
  await userEvent.click(screen.getByRole('button', { name: 'Place hold' }));
};

const stepUpWith = async (code = '123456') => {
  await userEvent.type(await screen.findByLabelText('Authenticator code'), code);
  await userEvent.click(screen.getByRole('form', { name: 'Step up' }).querySelector('button'));
};

/**
 * The retention screen (IA-REQ-056, C7).
 *
 * Two things run through all of it. One problem code answers two different situations — the read needs the factor
 * proved in this session, a change needs it proved recently — and nothing on the document tells them apart, so the
 * screen decides from the context it already holds. And the screen can stop an erasure but never order one: there
 * is no purge control here and there is no route behind one.
 */
describe('platform retention page', () => {
  afterEach(() => server.events.removeAllListeners());

  /**
   * A session that has not proved the factor is refused the policy read with `recent_mfa_required`. The screen
   * knows that from the context before asking, so it asks for nothing and offers the one thing that would help.
   */
  it('asks for the second factor instead of reading the policy', async () => {
    const paths = trackRequests();
    server.use(antiforgery(), contextIs(retentionContext(RETENTION_PERMISSIONS, { requiresTwoFactor: true })), policyIs());

    renderPage();

    expect(await screen.findByRole('form', { name: 'Step up' })).toBeInTheDocument();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
    expect(screen.queryByRole('form', { name: 'Place a hold' })).not.toBeInTheDocument();
    await waitFor(() => expect(retentionCalls(paths)).toEqual([]));
  });

  it('binds an invalid authenticator code to its field exactly once and clears it on edit', async () => {
    server.use(antiforgery(), contextIs(retentionContext(RETENTION_PERMISSIONS, { requiresTwoFactor: true })), policyIs());
    server.use(http.post('/api/platform/mfa/step-up', () => problem(400, 'invalid_mfa_code')));

    renderPage();
    const code = await screen.findByLabelText('Authenticator code');
    await userEvent.type(code, '000000');
    await userEvent.click(screen.getByRole('button', { name: 'Step up' }));

    const refusal = 'That authenticator code was not accepted. Check the code and try again.';
    expect(await screen.findAllByText(refusal)).toHaveLength(1);
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    expect(code).toHaveAttribute('aria-invalid', 'true');
    expect(code).toHaveAccessibleDescription(refusal);
    expect(code).toHaveFocus();
    expect(screen.getByRole('heading', { name: 'Retention' })).toBeInTheDocument();
    expect(screen.getByRole('form', { name: 'Step up' })).toBeInTheDocument();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();

    await userEvent.type(code, '1');

    expect(code).not.toHaveAttribute('aria-invalid', 'true');
    expect(code).not.toHaveAccessibleDescription(refusal);
    expect(screen.queryByText(refusal)).not.toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  /**
   * The proof is the server's to confirm. A step-up the API accepted whose reloaded context still owes the factor
   * leaves the gate exactly where it was — pretending otherwise would put the screen and the API into disagreement
   * about who has proved what.
   */
  it('keeps the gate up when the reloaded context still owes the factor', async () => {
    const paths = trackRequests();
    server.use(antiforgery(), policyIs());
    server.use(http.get('/api/identity/context', () =>
      HttpResponse.json(retentionContext(RETENTION_PERMISSIONS, { requiresTwoFactor: true }))));
    server.use(http.post('/api/platform/mfa/step-up', () => new HttpResponse(null, { status: 204 })));

    renderPage();
    await stepUpWith();

    expect(await screen.findByRole('form', { name: 'Step up' })).toBeInTheDocument();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
    await waitFor(() => expect(retentionCalls(paths)).toEqual([]));
  });

  it('associates and focuses a structured entry step-up detail without repeating it', async () => {
    const paths = trackRequests();
    server.use(antiforgery(), contextIs(retentionContext(RETENTION_PERMISSIONS, { requiresTwoFactor: true })), policyIs());
    server.use(http.post('/api/platform/mfa/step-up', () => problem(400, 'validation_failed', {
      errors: { code: [{ code: 'required', params: {} }] },
    })));

    renderPage();
    const code = await screen.findByLabelText('Authenticator code');
    await userEvent.type(code, '000000');
    await userEvent.click(screen.getByRole('button', { name: 'Step up' }));

    await waitFor(() => expect(code).toHaveAttribute('aria-invalid', 'true'));
    expect(code).toHaveAccessibleDescription('An authenticator code is required.');
    expect(code).toHaveFocus();
    expect(screen.getByRole('alert')).not.toHaveTextContent('An authenticator code is required.');
    expect(retentionCalls(paths)).toEqual([]);
  });

  /** A reload that did not come back says nothing about the factor either way, so it does not open anything. */
  it('keeps the gate up when the context reload does not come back', async () => {
    const paths = trackRequests();
    let signedIn = true;
    server.use(antiforgery(), policyIs());
    server.use(http.get('/api/identity/context', () => (signedIn
      ? HttpResponse.json(retentionContext(RETENTION_PERMISSIONS, { requiresTwoFactor: true }))
      : HttpResponse.json({ code: 'invalid_session', traceId: 't' }, {
        status: 401, headers: { 'Content-Type': 'application/problem+json' },
      }))));
    server.use(http.post('/api/platform/mfa/step-up', () => {
      signedIn = false;
      return new HttpResponse(null, { status: 204 });
    }));

    renderPage();
    await stepUpWith();

    await waitFor(() => expect(screen.queryByRole('table')).not.toBeInTheDocument());
    expect(retentionCalls(paths)).toEqual([]);
  });

  it('reads the policy once the reloaded context says the factor is settled', async () => {
    let requiresTwoFactor = true;
    server.use(antiforgery(), policyIs());
    server.use(http.get('/api/identity/context', () =>
      HttpResponse.json(retentionContext(RETENTION_PERMISSIONS, { requiresTwoFactor }))));
    server.use(http.post('/api/platform/mfa/step-up', () => {
      requiresTwoFactor = false;
      return new HttpResponse(null, { status: 204 });
    }));

    renderPage();
    await stepUpWith();

    expect(await screen.findByRole('table')).toBeInTheDocument();
    expect(await screen.findByText('Audit events')).toBeInTheDocument();
  });

  it('renders the personal data mode, the active hold count and the category rules', async () => {
    server.use(antiforgery(), contextIs(retentionContext()), policyIs());

    renderPage();

    expect(await screen.findByTestId('retention-personal-data-mode')).toHaveTextContent('Synthetic');
    expect(screen.getByTestId('retention-active-holds')).toHaveTextContent('2');
    const headers = screen.getAllByRole('columnheader').map((header) => header.textContent);
    expect(headers).toEqual(['Category', 'Retention period', 'Trigger', 'Action', 'Evidence required']);
    screen.getAllByRole('columnheader').forEach((header) => expect(header).toHaveAttribute('scope', 'col'));
    expect(screen.getByText('P2Y')).toBeInTheDocument();
    expect(screen.getByText('Session records')).toBeInTheDocument();
  });

  /**
   * A deployment with no policy at all is a loaded answer, not an empty one and not a refusal. An empty table would
   * read as "no categories", which is a different and much smaller statement than "nothing here will be erased".
   */
  it('says a deployment has no policy as a loaded state, with the hold count still on screen', async () => {
    server.use(antiforgery(), contextIs(retentionContext()), policyIs(NO_POLICY));

    renderPage();

    expect(await screen.findByText(/no retention policy is configured/i)).toHaveTextContent(/nothing will be erased/i);
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    expect(screen.getByTestId('retention-active-holds')).toHaveTextContent('4');
    expect(screen.getByTestId('retention-personal-data-mode')).toHaveTextContent('Real');
  });

  it('names the permission it needs and asks for nothing without it', async () => {
    const paths = trackRequests();
    server.use(antiforgery(), contextIs(retentionContext(['platform.organizations.read'])), policyIs());

    renderPage();

    expect(await screen.findByText('This screen needs the platform.retention.read permission. Ask a Platform owner to grant it.')).toBeInTheDocument();
    expect(screen.getByText('View retention policy', { exact: true })).toBeVisible();
    expect(screen.getByText('platform.retention.read', { exact: true })).toBeVisible();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
    await waitFor(() => expect(retentionCalls(paths)).toEqual([]));
  });

  /** Reading the policy and stopping an erasure are separately trusted, and manage does not follow from read. */
  it('offers no place-hold form and no release control to a reader who cannot manage', async () => {
    server.use(antiforgery(), contextIs(retentionContext(['platform.retention.read'])), policyIs());

    renderPage();

    expect(await screen.findByText('Audit events')).toBeInTheDocument();
    expect(screen.queryByRole('form', { name: 'Place a hold' })).not.toBeInTheDocument();
    expect(screen.queryByRole('form', { name: 'Release a hold' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /release/i })).not.toBeInTheDocument();
  });

  it('says it is reading before the policy arrives', async () => {
    server.use(antiforgery(), contextIs(retentionContext()));
    server.use(http.get('/api/platform/retention/policy', async () => { await delay('infinite'); }));

    renderPage();

    expect(await screen.findByRole('status')).toHaveTextContent(/reading the retention policy/i);
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  /** A refusal is not an empty screen, and it is not offered a retry that would only be refused again. */
  it('says why the policy could not be read instead of showing an empty one', async () => {
    server.use(antiforgery(), contextIs(retentionContext()));
    server.use(http.get('/api/platform/retention/policy', () => problem(403, 'permission_denied')));

    renderPage();

    expect(await screen.findByRole('alert')).toHaveTextContent(/do not have permission/i);
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
    expect(screen.queryByTestId('retention-active-holds')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Try again' })).not.toBeInTheDocument();
  });

  /** A success document that drifts from its contract is unreadable, not a raw parser diagnostic. */
  it('offers a safe retry when the read success document drifts from its contract', async () => {
    let attempt = 0;
    server.use(antiforgery(), contextIs(retentionContext()));
    server.use(http.get('/api/platform/retention/policy', () => {
      attempt += 1;
      return attempt === 1
        ? HttpResponse.json({ personalDataMode: 'Synthetic', activeHoldCount: 0 })
        : HttpResponse.json(POLICY);
    }));

    renderPage();

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('We could not read the answer. Reload the page and try again.');
    expect(alert).not.toHaveTextContent('categories');
    await userEvent.click(screen.getByRole('button', { name: 'Try again' }));

    expect(await screen.findByText('Audit events')).toBeInTheDocument();
  });

  it('sends no request for a reason code outside the shape the server accepts', async () => {
    const posts = [];
    server.use(antiforgery(), contextIs(retentionContext()), policyIs());
    server.use(http.post('/api/platform/retention/holds', async ({ request }) => {
      posts.push(await request.json());
      return HttpResponse.json(HOLD, { status: 201 });
    }));

    renderPage();
    await placeAHold({ reasonCode: 'not a code!' });

    expect(await screen.findByRole('alert')).toHaveTextContent(/letters, digits/i);
    expect(screen.getByLabelText('Reason code')).toHaveAccessibleDescription(/letters, digits/i);
    expect(posts).toEqual([]);
  });

  it('sends no request for a reference outside the shape the server accepts', async () => {
    const posts = [];
    server.use(antiforgery(), contextIs(retentionContext()), policyIs());
    server.use(http.post('/api/platform/retention/holds', async ({ request }) => {
      posts.push(await request.json());
      return HttpResponse.json(HOLD, { status: 201 });
    }));

    renderPage();
    await placeAHold({ reference: 'case 2026/114' });

    expect(await screen.findByRole('alert')).toHaveTextContent(/letters, digits/i);
    expect(screen.getByLabelText('Reference')).toHaveAccessibleDescription(/letters, digits/i);
    expect(posts).toEqual([]);
  });

  /** The local check mirrors the server's; it never becomes the authority. A value it lets through can still be refused. */
  it('shows the server refusal of a locally valid value rather than claiming it was accepted', async () => {
    server.use(antiforgery(), contextIs(retentionContext()), policyIs());
    server.use(http.post('/api/platform/retention/holds', () => problem(400, 'invalid_platform_operation')));

    renderPage();
    await placeAHold();

    expect(await screen.findByRole('alert')).toHaveTextContent(/not valid in its current state/i);
    expect(screen.queryByRole('status')).not.toBeInTheDocument();
  });

  /**
   * The receipt is the only time a hold id is ever shown: no route lists holds, so an operator who does not write
   * this down has no way to name the hold again.
   */
  it('shows a receipt naming the hold id, reason, reference and when it was placed', async () => {
    server.use(antiforgery(), contextIs(retentionContext()), policyIs());
    server.use(http.post('/api/platform/retention/holds', () => HttpResponse.json(HOLD, { status: 201 })));

    renderPage();
    await placeAHold();

    const receipt = await screen.findByRole('status');
    expect(receipt).toHaveTextContent(HOLD.holdId);
    expect(receipt).toHaveTextContent(HOLD.reasonCode);
    expect(receipt).toHaveTextContent(HOLD.reference);
    expect(receipt).toHaveTextContent(new Intl.DateTimeFormat('en', { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(HOLD.placedAt)));
  });

  it('keeps mutation controls disabled until the post-mutation policy read settles', async () => {
    const refreshed = deferred();
    let policyReads = 0;
    let placements = 0;
    server.use(antiforgery(), contextIs(retentionContext()));
    server.use(http.get('/api/platform/retention/policy', () => {
      policyReads += 1;
      return policyReads === 1 ? HttpResponse.json(POLICY) : refreshed.promise;
    }));
    server.use(http.post('/api/platform/retention/holds', () => {
      placements += 1;
      return HttpResponse.json(HOLD, { status: 201 });
    }));

    renderPage();
    await placeAHold();
    await waitFor(() => expect(policyReads).toBe(2));
    expect(placements).toBe(1);

    const place = screen.getByRole('button', { name: 'Place hold' });
    expect(place).toBeDisabled();
    place.click();
    expect(placements).toBe(1);

    refreshed.resolve(HttpResponse.json(POLICY));
    expect(await screen.findByRole('status')).toHaveTextContent(HOLD.holdId);
    await waitFor(() => expect(place).toBeEnabled());
  });

  /**
   * "A hold already stands" and "those records are already gone" are two different facts about two different
   * futures, and an operator acts differently on each. One sentence for both would hide that.
   */
  it('tells a standing hold and an already-erased subject apart', async () => {
    server.use(antiforgery(), contextIs(retentionContext()), policyIs());
    server.use(http.post('/api/platform/retention/holds', () => problem(409, 'retention_hold_conflict')));

    const conflict = renderPage();
    await placeAHold();
    const standing = (await screen.findByRole('alert')).textContent;
    conflict.unmount();

    server.use(http.post('/api/platform/retention/holds', () => problem(409, 'retention_hold_subject_purged')));
    renderPage();
    await placeAHold();
    const purged = (await screen.findByRole('alert')).textContent;

    expect(standing).toMatch(/hold with that reason already stands/i);
    expect(purged).toMatch(/already erased/i);
    expect(standing).not.toEqual(purged);
  });

  /** A step-up resolves exactly one refusal. Offering it for the others would be an invitation to nothing. */
  it.each([
    ['permission_denied', 403],
    ['retention_hold_conflict', 409],
    ['retention_hold_subject_purged', 409],
    ['validation_failed', 400],
    ['service_unavailable', 503],
    ['invalid_platform_operation', 400],
    ['not_found', 404],
    ['rate_limit_exceeded', 429],
  ])('offers no step-up when the change was refused with %s', async (code, status) => {
    server.use(antiforgery(), contextIs(retentionContext()), policyIs());
    server.use(http.post('/api/platform/retention/holds', () => problem(status, code)));

    renderPage();
    await placeAHold();

    await screen.findByRole('alert');
    expect(screen.queryByRole('form', { name: 'Step up' })).not.toBeInTheDocument();
    expect(screen.getByRole('table')).toBeInTheDocument();
  });

  /**
   * The other side of the same code. Here the session HAS proved the factor, so what is missing is a recent proof
   * and the screen keeps everything it already read while asking for one.
   */
  it('keeps the policy on screen and offers a step-up when the change needs a recent proof', async () => {
    server.use(antiforgery(), contextIs(retentionContext()), policyIs());
    server.use(http.post('/api/platform/retention/holds', () => problem(401, 'recent_mfa_required')));

    renderPage();
    await placeAHold();

    expect(await screen.findByRole('form', { name: 'Step up' })).toBeInTheDocument();
    expect(screen.getByRole('alert')).toHaveTextContent(/second factor again/i);
    expect(screen.getByRole('table')).toBeInTheDocument();
    expect(screen.getByText('Audit events')).toBeInTheDocument();
  });

  /**
   * The gate refreshes the policy and stops. A change replayed by the proof that unblocked it is a change nobody
   * asked for twice, and a legal hold is not a thing to place by accident.
   */
  it('never replays the refused change once the step-up succeeds', async () => {
    const posts = [];
    server.use(antiforgery(), contextIs(retentionContext()), policyIs());
    server.use(http.post('/api/platform/retention/holds', async ({ request }) => {
      posts.push(await request.json());
      return problem(401, 'recent_mfa_required');
    }));
    server.use(http.post('/api/platform/mfa/step-up', () => new HttpResponse(null, { status: 204 })));

    renderPage();
    await placeAHold();
    await screen.findByRole('form', { name: 'Step up' });
    await stepUpWith();

    await waitFor(() => expect(screen.queryByRole('form', { name: 'Step up' })).not.toBeInTheDocument());
    expect(posts).toHaveLength(1);
    expect(screen.getByRole('table')).toBeInTheDocument();
  });

  it('does not release a hold until the release is confirmed', async () => {
    const releases = [];
    server.use(antiforgery(), contextIs(retentionContext()), policyIs());
    server.use(http.delete('/api/platform/retention/holds/:holdId', ({ params }) => {
      releases.push(params.holdId);
      return new HttpResponse(null, { status: 204 });
    }));

    renderPage();
    await userEvent.type(await screen.findByLabelText('Hold id'), HOLD.holdId);
    await userEvent.click(screen.getByRole('button', { name: 'Release' }));

    expect(await screen.findByRole('form', { name: 'Confirm release' })).toBeInTheDocument();
    expect(releases).toEqual([]);

    await userEvent.click(screen.getByRole('button', { name: 'Confirm release' }));
    await waitFor(() => expect(releases).toEqual([HOLD.holdId]));
  });

  /**
   * The route answers 204 for a hold it released, one already released, and one that never existed. So the screen
   * states the resulting state and nothing else: claiming this call released it, or that it existed, would be the
   * screen inventing an answer the API deliberately refused to give.
   */
  it('words a release as the resulting state, identically for a hold that never existed', async () => {
    server.use(antiforgery(), contextIs(retentionContext()), policyIs());
    server.use(http.delete('/api/platform/retention/holds/:holdId', () => new HttpResponse(null, { status: 204 })));

    const first = renderPage();
    await userEvent.type(await screen.findByLabelText('Hold id'), HOLD.holdId);
    await userEvent.click(screen.getByRole('button', { name: 'Release' }));
    await userEvent.click(await screen.findByRole('button', { name: 'Confirm release' }));
    const released = (await screen.findByRole('status')).textContent;
    first.unmount();

    renderPage();
    await userEvent.type(await screen.findByLabelText('Hold id'), '00000000-0000-0000-0000-000000000000');
    await userEvent.click(screen.getByRole('button', { name: 'Release' }));
    await userEvent.click(await screen.findByRole('button', { name: 'Confirm release' }));
    const unknown = (await screen.findByRole('status')).textContent;

    expect(released).toBe(unknown);
    expect(released).toMatch(/this hold is released/i);
    expect(released).toMatch(/never existed/i);
    expect(released).not.toMatch(/we released|released it|this request released|that hold existed/i);
  });

  /** A confirmation staged against a refused change is not the thing being answered any more. */
  it('tears the pending confirmation down before the step-up gate renders', async () => {
    server.use(antiforgery(), contextIs(retentionContext()), policyIs());
    server.use(http.delete('/api/platform/retention/holds/:holdId', () => problem(401, 'recent_mfa_required')));

    renderPage();
    await userEvent.type(await screen.findByLabelText('Hold id'), HOLD.holdId);
    await userEvent.click(screen.getByRole('button', { name: 'Release' }));
    await userEvent.click(await screen.findByRole('button', { name: 'Confirm release' }));

    expect(await screen.findByRole('form', { name: 'Step up' })).toBeInTheDocument();
    expect(screen.queryByRole('form', { name: 'Confirm release' })).not.toBeInTheDocument();
    expect(screen.getByRole('table')).toBeInTheDocument();
  });

  /**
   * The capabilities this screen must never grow. Erasure belongs to the maintenance worker, driven by policy:
   * there is no purge endpoint and no permission for one, so there is nothing here to press.
   */
  it('offers no purge, impersonation or tenant override, and names no tenant in any request', async () => {
    const bodies = [];
    const paths = trackRequests();
    server.use(antiforgery(), contextIs(retentionContext()), policyIs());
    server.use(http.post('/api/platform/retention/holds', async ({ request }) => {
      bodies.push(await request.json());
      return HttpResponse.json(HOLD, { status: 201 });
    }));

    renderPage();
    await placeAHold();
    await screen.findByRole('status');

    const controls = screen.getAllByRole('button').map((button) => button.textContent.toLowerCase());
    expect(controls.some((label) => /purge|erase|delete|impersonat|act as/.test(label))).toBe(false);
    expect(screen.queryByLabelText(/purge|erase|impersonat|act as|tenant/i)).not.toBeInTheDocument();
    expect(bodies).toEqual([{
      subjectIdentityId: HOLD.subjectIdentityId,
      reasonCode: HOLD.reasonCode,
      reference: HOLD.reference,
    }]);
    expect(paths.some((path) => /tenant/i.test(path))).toBe(false);
  });
});

import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import { IdentityProvider } from '../context/IdentityProvider';
import { PersonalProfilePage, PersonalRegisterPage } from './PersonalPages';
import { ChooseContextPage } from '../register/ChooseContextPage';
import { server } from '../../../test/server';
import { antiforgery, contextIs, problem, signedInContext } from '../../../test/identityServer';

const renderPage = (page) => render(
  <MemoryRouter><IdentityProvider>{page}</IdentityProvider></MemoryRouter>
);

const recordedProfile = (overrides = {}) => ({
  fullName: 'Jane Doe',
  displayName: 'Jane',
  email: 'jane@example.test',
  personalTenantId: '2f1d8a1e-0000-4000-8000-000000000001',
  document: { country: 'AR', type: 'DNI', status: 'recorded', maskedNumber: '••••••78', correctionAvailable: false },
  version: '42',
  updatedAt: '2026-09-06T12:00:00+00:00',
  ...overrides,
});

const deferred = () => {
  let resolve;
  const promise = new Promise((onResolve) => { resolve = onResolve; });
  return { promise, resolve };
};

/**
 * A person's own context in the browser: the choice that precedes it, the signup that stays neutral, and the
 * profile that shows a masked document and offers the one correction route there is — a request for review by a
 * second party, never an edit.
 */
describe('personal pages', () => {
  it('offers both kinds of registration rather than choosing for the visitor', () => {
    renderPage(<ChooseContextPage />);

    expect(screen.getByRole('link', { name: /a personal account/i })).toHaveAttribute('href', '/personal/register');
    expect(screen.getByRole('link', { name: /an organization/i })).toHaveAttribute('href', '/organizations/register');
  });

  it('sends the signup and then says the same neutral thing it would say for a taken address', async () => {
    const submissions = [];
    server.use(antiforgery(), contextIs(null));
    server.use(http.post('/api/identity/personal/register', async ({ request }) => {
      submissions.push(await request.json());
      return new HttpResponse(null, { status: 202 });
    }));

    renderPage(<PersonalRegisterPage />);
    await userEvent.type(screen.getByLabelText('Full name'), 'Jane Doe');
    await userEvent.type(screen.getByLabelText('Display name'), 'Jane');
    await userEvent.type(screen.getByLabelText('DNI'), '12.345.678');
    await userEvent.type(screen.getByLabelText('Email'), 'jane@example.test');
    await userEvent.type(screen.getByLabelText('Password'), 'Testing1234!');
    await userEvent.click(screen.getByRole('button', { name: 'Register' }));

    await waitFor(() => expect(submissions).toHaveLength(1));
    expect(submissions[0]).toEqual({
      fullName: 'Jane Doe',
      displayName: 'Jane',
      documentNumber: '12.345.678',
      email: 'jane@example.test',
      password: 'Testing1234!',
    });
    expect(await screen.findByRole('status')).toHaveTextContent(/we have sent it a confirmation link/i);
  });

  it('blocks client-invalid signup with exact field errors and focuses the first field', async () => {
    const submissions = [];
    server.use(antiforgery(), contextIs(null));
    server.use(http.post('/api/identity/personal/register', async ({ request }) => {
      submissions.push(await request.json());
      return new HttpResponse(null, { status: 202 });
    }));

    renderPage(<PersonalRegisterPage />);
    await userEvent.click(screen.getByRole('button', { name: 'Register' }));

    const fullName = screen.getByLabelText('Full name');
    expect(submissions).toHaveLength(0);
    expect(fullName).toHaveFocus();
    expect(fullName).toHaveAttribute('aria-invalid', 'true');
    expect(fullName).toHaveAccessibleDescription('This value is required.');
    expect(screen.getByLabelText('Display name')).toHaveAccessibleDescription('This value is required.');
    expect(screen.getByLabelText('DNI')).toHaveAccessibleDescription('This value is required.');
    expect(screen.getByLabelText('Email')).toHaveAccessibleDescription('This value is required.');
    expect(screen.getByLabelText('Password')).toHaveAccessibleDescription('This value is required.');
  });

  it('binds exact signup errors, keeps an unclaimed summary, focuses, and clears one edited field', async () => {
    server.use(antiforgery(), contextIs(null));
    server.use(http.post('/api/identity/personal/register', () => problem(400, 'validation_failed', {
      status: 400,
      errors: {
        fullName: [{ code: 'required', params: {} }],
        displayName: [{ code: 'required', params: {} }],
        documentNumber: [{ code: 'invalid', params: {} }],
        email: [{ code: 'invalid', params: {} }],
        password: [{ code: 'password_policy', params: {} }],
        request: [{ code: 'invalid', params: {} }],
      },
    })));

    renderPage(<PersonalRegisterPage />);
    await userEvent.type(screen.getByLabelText('Full name'), 'Jane Doe');
    await userEvent.type(screen.getByLabelText('Display name'), 'Jane');
    await userEvent.type(screen.getByLabelText('DNI'), '12.345.678');
    await userEvent.type(screen.getByLabelText('Email'), 'jane@example.test');
    await userEvent.type(screen.getByLabelText('Password'), 'Testing1234!');
    await userEvent.click(screen.getByRole('button', { name: 'Register' }));

    const fullName = await screen.findByLabelText('Full name');
    const displayName = screen.getByLabelText('Display name');
    const documentNumber = screen.getByLabelText('DNI');
    const email = screen.getByLabelText('Email');
    const passwordField = screen.getByLabelText('Password');
    await waitFor(() => expect(fullName).toHaveFocus());
    expect(fullName).toHaveAttribute('aria-invalid', 'true');
    expect(fullName).toHaveAccessibleDescription('This value is required.');
    expect(displayName).toHaveAccessibleDescription('This value is required.');
    expect(documentNumber).toHaveAccessibleDescription('This value is not valid.');
    expect(email).toHaveAccessibleDescription('This value is not valid.');
    expect(passwordField).toHaveAccessibleDescription('This password does not meet the requirements.');

    const alert = screen.getByRole('alert');
    expect(alert).toHaveTextContent('Request: This value is not valid.');
    expect(alert).not.toHaveTextContent('request:');
    expect(alert).not.toHaveTextContent('Full name: This value is required.');
    expect(alert).not.toHaveTextContent('Password: This password does not meet the requirements.');

    await userEvent.type(displayName, 'x');
    expect(displayName).not.toHaveAttribute('aria-invalid', 'true');
    expect(displayName).not.toHaveAccessibleDescription('This value is required.');
    expect(documentNumber).toHaveAttribute('aria-invalid', 'true');
  });

  it('never leaves the document or the password in browser storage', async () => {
    server.use(antiforgery(), contextIs(null));
    server.use(http.post('/api/identity/personal/register', () => new HttpResponse(null, { status: 202 })));

    renderPage(<PersonalRegisterPage />);
    await userEvent.type(screen.getByLabelText('DNI'), '12345678');
    await userEvent.type(screen.getByLabelText('Password'), 'Testing1234!');

    expect(JSON.stringify(window.localStorage)).not.toContain('12345678');
    expect(JSON.stringify(window.sessionStorage)).not.toContain('Testing1234!');
  });

  it('shows the document masked and says a correction is already being reviewed', async () => {
    server.use(antiforgery(), contextIs(signedInContext()));
    server.use(http.get('/api/identity/profile', () => HttpResponse.json(recordedProfile())));

    renderPage(<PersonalProfilePage />);

    expect(await screen.findByText('••••••78')).toBeInTheDocument();
    expect(screen.getByText('AR DNI')).toBeInTheDocument();
    // The fixture reports `correctionAvailable: false`, which now means one is open rather than none is possible.
    expect(screen.getByText(/already being reviewed/i)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /send for review/i })).not.toBeInTheDocument();
  });

  it('offers the correction when this person has none open, and sends the claim once', async () => {
    const claims = [];
    server.use(antiforgery(), contextIs(signedInContext()));
    server.use(http.get('/api/identity/profile', () =>
      HttpResponse.json({ ...recordedProfile(), document: { ...recordedProfile().document, correctionAvailable: true } })));
    server.use(http.post('/api/identity/profile/document/disputes', async ({ request }) => {
      claims.push(await request.json());
      return HttpResponse.json({ disputeId: '11111111-1111-1111-1111-111111111111' }, { status: 201 });
    }));

    renderPage(<PersonalProfilePage />);

    await userEvent.type(await screen.findByLabelText(/what the number should be/i), '30111333');
    await userEvent.click(screen.getByRole('button', { name: /send for review/i }));

    expect(await screen.findByText(/sent for review/i)).toBeInTheDocument();
    expect(claims).toHaveLength(1);
    expect(claims[0]).toEqual({
      claimedCountry: 'AR',
      claimedType: 'DNI',
      claimedNumber: '30111333',
      reasonCode: 'TypedWrongAtSignup',
    });

    // The number left the browser once and nothing on the screen keeps it.
    expect(screen.queryByDisplayValue('30111333')).not.toBeInTheDocument();
  });

  it('edits only the two permitted names and echoes the version it read', async () => {
    const edits = [];
    server.use(antiforgery(), contextIs(signedInContext()));
    server.use(http.get('/api/identity/profile', () => HttpResponse.json(recordedProfile())));
    server.use(http.put('/api/identity/profile', async ({ request }) => {
      edits.push(await request.json());
      return HttpResponse.json(recordedProfile({ fullName: 'Jane Q. Doe', displayName: 'Janie', version: '43' }));
    }));

    renderPage(<PersonalProfilePage />);
    const fullName = await screen.findByLabelText('Full name');
    await userEvent.clear(fullName);
    await userEvent.type(fullName, 'Jane Q. Doe');
    const displayName = screen.getByLabelText('Display name');
    await userEvent.clear(displayName);
    await userEvent.type(displayName, 'Janie');
    await userEvent.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(edits).toHaveLength(1));
    expect(edits[0]).toEqual({ fullName: 'Jane Q. Doe', displayName: 'Janie', version: '42' });
    expect(Object.keys(edits[0])).not.toContain('documentNumber');
    expect(Object.keys(edits[0])).not.toContain('email');
    await waitFor(() => expect(screen.getByLabelText('Display name')).toHaveValue('Janie'));
  });

  it('keeps both submitted names and their version after a save refusal', async () => {
    const edits = [];
    server.use(antiforgery(), contextIs(signedInContext()));
    server.use(http.get('/api/identity/profile', () => HttpResponse.json(recordedProfile())));
    server.use(http.put('/api/identity/profile', async ({ request }) => {
      edits.push(await request.json());
      return problem(409, 'personal_profile_concurrency_conflict');
    }));

    renderPage(<PersonalProfilePage />);
    const fullName = await screen.findByLabelText('Full name');
    await userEvent.clear(fullName);
    await userEvent.type(fullName, 'Jane Q. Doe');
    const displayName = screen.getByLabelText('Display name');
    await userEvent.clear(displayName);
    await userEvent.type(displayName, 'Janie');
    const save = screen.getByRole('button', { name: 'Save' });
    const profileForm = save.closest('form');
    await userEvent.click(save);

    expect(await within(profileForm).findByRole('alert')).toHaveTextContent(/changed while you were editing/i);
    expect(fullName).toHaveValue('Jane Q. Doe');
    expect(displayName).toHaveValue('Janie');
    expect(edits).toEqual([{ fullName: 'Jane Q. Doe', displayName: 'Janie', version: '42' }]);
  });

  it('adopts normalized saved fields without erasing a newer in-flight draft', async () => {
    const save = deferred();
    const edits = [];
    server.use(antiforgery(), contextIs(signedInContext()));
    server.use(http.get('/api/identity/profile', () => HttpResponse.json(recordedProfile())));
    server.use(http.put('/api/identity/profile', async ({ request }) => {
      edits.push(await request.json());
      await save.promise;
      return HttpResponse.json(recordedProfile({ fullName: 'Jane Q. Doe', displayName: 'JANIE', version: '43' }));
    }));

    renderPage(<PersonalProfilePage />);
    const fullName = await screen.findByLabelText('Full name');
    await userEvent.clear(fullName);
    await userEvent.type(fullName, 'Jane Q Doe');
    const displayName = screen.getByLabelText('Display name');
    await userEvent.clear(displayName);
    await userEvent.type(displayName, 'Janie');
    await userEvent.click(screen.getByRole('button', { name: 'Save' }));
    await waitFor(() => expect(edits).toHaveLength(1));

    await userEvent.clear(displayName);
    await userEvent.type(displayName, 'Janet');
    save.resolve();

    await waitFor(() => expect(fullName).toHaveValue('Jane Q. Doe'));
    expect(displayName).toHaveValue('Janet');
    expect(edits[0]).toEqual({ fullName: 'Jane Q Doe', displayName: 'Janie', version: '42' });
  });

  it('binds editable profile fields, focuses in form order, and leaves version unclaimed', async () => {
    server.use(antiforgery(), contextIs(signedInContext()));
    server.use(http.get('/api/identity/profile', () => HttpResponse.json(recordedProfile())));
    server.use(http.put('/api/identity/profile', () => problem(400, 'validation_failed', {
      status: 400,
      errors: {
        fullName: [{ code: 'required', params: {} }],
        displayName: [{ code: 'required', params: {} }],
        version: [{ code: 'invalid', params: {} }],
      },
    })));

    renderPage(<PersonalProfilePage />);
    await userEvent.click(await screen.findByRole('button', { name: 'Save' }));

    const fullName = screen.getByLabelText('Full name');
    await waitFor(() => expect(fullName).toHaveFocus());
    expect(fullName).toHaveAccessibleDescription('This value is required.');
    expect(screen.getByLabelText('Display name')).toHaveAccessibleDescription('This value is required.');
    const profileForm = screen.getByRole('button', { name: 'Save' }).closest('form');
    const alert = within(profileForm).getByRole('alert');
    expect(alert).toHaveTextContent('Field: This value is not valid.');
    expect(alert).not.toHaveTextContent('Full name: This value is required.');

    await userEvent.type(fullName, 'x');
    expect(fullName).not.toHaveAttribute('aria-invalid', 'true');
  });

  it('tells an identity with no personal context how to make one instead of showing an error', async () => {
    server.use(antiforgery(), contextIs(signedInContext()));
    server.use(http.get('/api/identity/profile', () => problem(404, 'personal_profile_not_found')));

    renderPage(<PersonalProfilePage />);

    expect(await screen.findByRole('status')).toHaveTextContent(/no personal context yet/i);
  });

  /**
   * Somebody who is already signed in does not register again. Pointing them at the newcomer signup asks for an
   * address and a password they already have, and the route behind it answers the neutral acknowledgement a
   * stranger gets — so the person who wanted a personal account is told nothing and given none.
   */
  it('lets a signed-in identity add a personal context without registering a second time', async () => {
    server.use(antiforgery(), contextIs(signedInContext()));
    server.use(http.get('/api/identity/profile', () => problem(404, 'personal_profile_not_found')));
    const claims = [];
    server.use(http.post('/api/identity/personal', async ({ request }) => {
      claims.push(await request.json());
      return new HttpResponse(null, { status: 204 });
    }));

    renderPage(<PersonalProfilePage />);
    await screen.findByRole('status');
    await userEvent.type(screen.getByLabelText('Full name'), 'Jane Doe');
    await userEvent.type(screen.getByLabelText('Display name'), 'Jane');
    await userEvent.type(screen.getByLabelText('DNI'), '30111222');
    await userEvent.click(screen.getByRole('button', { name: /add my personal account/i }));

    await waitFor(() => expect(claims).toHaveLength(1));
    expect(claims[0]).toEqual({ fullName: 'Jane Doe', displayName: 'Jane', documentNumber: '30111222' });
    expect(Object.keys(claims[0])).not.toContain('email');
    expect(Object.keys(claims[0])).not.toContain('password');
  });

  it('binds add-personal field errors and focuses the first field in form order', async () => {
    server.use(antiforgery(), contextIs(signedInContext()));
    server.use(http.get('/api/identity/profile', () => problem(404, 'personal_profile_not_found')));
    server.use(http.post('/api/identity/personal', () => problem(400, 'validation_failed', {
      status: 400,
      errors: {
        fullName: [{ code: 'required', params: {} }],
        displayName: [{ code: 'required', params: {} }],
        documentNumber: [{ code: 'invalid', params: {} }],
      },
    })));

    renderPage(<PersonalProfilePage />);
    await screen.findByRole('status');
    await userEvent.type(screen.getByLabelText('Full name'), 'x');
    await userEvent.type(screen.getByLabelText('Display name'), 'Jane');
    await userEvent.type(screen.getByLabelText('DNI'), '30111222');
    await userEvent.click(screen.getByRole('button', { name: /add my personal account/i }));

    const fullName = screen.getByLabelText('Full name');
    await waitFor(() => expect(fullName).toHaveFocus());
    expect(fullName).toHaveAccessibleDescription('This value is required.');
    expect(screen.getByLabelText('Display name')).toHaveAccessibleDescription('This value is required.');
    expect(screen.getByLabelText('DNI')).toHaveAccessibleDescription('This value is not valid.');
    const alert = screen.getByRole('alert');
    expect(alert).toHaveTextContent('Some of what you sent was not accepted. Check the details and try again.');
    expect(alert).not.toHaveTextContent('This value is required.');
    expect(alert).not.toHaveTextContent('This value is not valid.');

    await userEvent.type(screen.getByLabelText('Display name'), 'x');
    expect(screen.getByLabelText('Display name')).not.toHaveAttribute('aria-invalid', 'true');
  });

  it('reports a personal registration conflict without identifying another account', async () => {
    server.use(antiforgery(), contextIs(signedInContext()));
    server.use(http.get('/api/identity/profile', () => problem(404, 'personal_profile_not_found')));
    server.use(http.post('/api/identity/personal', () => problem(409, 'personal_registration_conflict')));

    renderPage(<PersonalProfilePage />);
    await screen.findByRole('status');
    await userEvent.type(screen.getByLabelText('Full name'), 'Jane Doe');
    await userEvent.type(screen.getByLabelText('Display name'), 'Jane');
    await userEvent.type(screen.getByLabelText('DNI'), '30111222');
    await userEvent.click(screen.getByRole('button', { name: /add my personal account/i }));

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Your personal account could not be created. If this address is confirmed, sign in and try again.',
    );
  });
});

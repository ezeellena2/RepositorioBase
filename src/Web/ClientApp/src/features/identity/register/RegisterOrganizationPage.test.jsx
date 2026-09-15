import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import { IdentityProvider } from '../context/IdentityProvider';
import { antiforgery, contextIs, problem, signedInContext } from '../../../test/identityServer';
import { server } from '../../../test/server';
import { RegisterOrganizationPage } from './RegisterOrganizationPage';

const renderPage = () => render(
  <MemoryRouter><IdentityProvider><RegisterOrganizationPage /></IdentityProvider></MemoryRouter>,
);

const fillValidForm = async () => {
  await userEvent.type(await screen.findByLabelText('Legal name'), 'Northwind SA');
  await userEvent.type(screen.getByLabelText('CUIT'), '30-12345678-1');
  await userEvent.type(screen.getByLabelText('Email'), 'owner@example.test');
  await userEvent.type(screen.getByLabelText('Password'), 'Testing1234!');
};

describe('organization registration', () => {
  it('does not render credential fields before a signed-in context resolves', async () => {
    let releaseContext;
    const contextReady = new Promise((resolve) => { releaseContext = resolve; });
    server.use(antiforgery());
    server.use(http.get('/api/identity/context', async () => {
      await contextReady;
      return HttpResponse.json(signedInContext());
    }));

    renderPage();
    try {
      expect(screen.queryByLabelText('Email')).not.toBeInTheDocument();
      expect(screen.queryByLabelText('Password')).not.toBeInTheDocument();
    } finally {
      releaseContext();
    }

    await screen.findByLabelText('Legal name');
    expect(screen.queryByLabelText('Email')).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Password')).not.toBeInTheDocument();
  });

  it('focuses the CUIT first', async () => {
    const submissions = [];
    server.use(antiforgery(), contextIs(null));
    server.use(http.post('/api/identity/organizations/register', async ({ request }) => {
      submissions.push(await request.json());
      return new HttpResponse(null, { status: 202 });
    }));

    renderPage();
    await userEvent.click(await screen.findByRole('button', { name: 'Register' }));

    expect(submissions).toHaveLength(0);
    expect(screen.getByLabelText('CUIT')).toHaveFocus();
    await userEvent.tab();
    expect(screen.getByLabelText('Legal name')).toHaveFocus();
    await userEvent.tab();
    expect(screen.getByLabelText('Email')).toHaveFocus();
    await userEvent.tab();
    expect(screen.getByLabelText('Password')).toHaveFocus();
  });

  it('blocks client-invalid input with exact field errors and focuses the first field', async () => {
    const submissions = [];
    server.use(antiforgery(), contextIs(null));
    server.use(http.post('/api/identity/organizations/register', async ({ request }) => {
      submissions.push(await request.json());
      return new HttpResponse(null, { status: 202 });
    }));

    renderPage();
    await userEvent.click(await screen.findByRole('button', { name: 'Register' }));

    const legalName = screen.getByLabelText('Legal name');
    expect(submissions).toHaveLength(0);
    expect(screen.getByLabelText('CUIT')).toHaveFocus();
    expect(legalName).toHaveAttribute('aria-invalid', 'true');
    expect(legalName).toHaveAccessibleDescription('A legal name is required.');
    expect(screen.getByLabelText('CUIT')).toHaveAccessibleDescription('A CUIT is required.');
    expect(screen.getByLabelText('Email')).toHaveAccessibleDescription('Enter an email address.');
    expect(screen.getByLabelText('Password')).toHaveAccessibleDescription('A password is required.');
  });

  it('binds exact server errors, summarizes only unclaimed errors, focuses, and clears one edited field', async () => {
    server.use(antiforgery(), contextIs(null));
    server.use(http.post('/api/identity/organizations/register', () => problem(400, 'validation_failed', {
      status: 400,
      errors: {
        legalName: [{ code: 'required', params: {} }],
        cuit: [{ code: 'cuit_check_digit', params: {} }],
        email: [{ code: 'email_format', params: {} }],
        password: [{ code: 'password_too_short', params: { min: 12 } }],
        request: [{ code: 'invalid', params: {} }],
      },
    })));

    renderPage();
    await fillValidForm();
    await userEvent.click(screen.getByRole('button', { name: 'Register' }));

    const legalName = await screen.findByLabelText('Legal name');
    const cuit = screen.getByLabelText('CUIT');
    const email = screen.getByLabelText('Email');
    const passwordField = screen.getByLabelText('Password');
    await waitFor(() => expect(cuit).toHaveFocus());
    expect(legalName).toHaveAttribute('aria-invalid', 'true');
    expect(legalName).toHaveAccessibleDescription('A legal name is required.');
    expect(cuit).toHaveAccessibleDescription("That CUIT's check digit does not match. Check the number.");
    expect(email).toHaveAccessibleDescription('Enter an email address.');
    expect(passwordField).toHaveAccessibleDescription('Passwords must be at least 12 characters.');

    const alert = screen.getByRole('alert');
    expect(alert).toHaveTextContent('This value is not valid.');
    expect(alert).not.toHaveTextContent('Request:');
    expect(alert).not.toHaveTextContent('Legal name: A legal name is required.');
    expect(alert).not.toHaveTextContent('Password: Passwords must be at least 12 characters.');

    await userEvent.type(email, 'x');
    expect(email).not.toHaveAttribute('aria-invalid', 'true');
    expect(email).not.toHaveAccessibleDescription('Enter an email address.');
    expect(cuit).toHaveAttribute('aria-invalid', 'true');
  });

  it('marks a wrong CUIT check digit, focuses it, and sends nothing', async () => {
    const submissions = [];
    server.use(antiforgery(), contextIs(null));
    server.use(http.post('/api/identity/organizations/register', async ({ request }) => {
      submissions.push(await request.json());
      return new HttpResponse(null, { status: 202 });
    }));

    renderPage();
    await userEvent.type(await screen.findByLabelText('Legal name'), 'Northwind SA');
    await userEvent.type(screen.getByLabelText('CUIT'), '30-12345678-9');
    await userEvent.type(screen.getByLabelText('Email'), 'owner@example.test');
    await userEvent.type(screen.getByLabelText('Password'), 'Testing1234!');
    await userEvent.click(screen.getByRole('button', { name: 'Register' }));

    expect(submissions).toHaveLength(0);
    const cuit = screen.getByLabelText('CUIT');
    expect(cuit).toHaveAttribute('aria-invalid', 'true');
    expect(cuit).toHaveAccessibleDescription("That CUIT's check digit does not match. Check the number.");
    expect(cuit).toHaveFocus();
  });

  it('keeps the neutral success and exact request payload', async () => {
    const submissions = [];
    server.use(antiforgery(), contextIs(null));
    server.use(http.post('/api/identity/organizations/register', async ({ request }) => {
      submissions.push(await request.json());
      return new HttpResponse(null, { status: 202 });
    }));

    renderPage();
    await fillValidForm();
    await userEvent.click(screen.getByRole('button', { name: 'Register' }));

    await waitFor(() => expect(submissions).toHaveLength(1));
    expect(submissions[0]).toEqual({
      legalName: 'Northwind SA',
      cuit: '30-12345678-1',
      email: 'owner@example.test',
      password: 'Testing1234!',
    });
    expect(await screen.findByRole('status')).toHaveTextContent(/we have sent it a confirmation link/i);
    expect(screen.getByRole('link', { name: 'Sign in' })).toHaveAttribute(
      'href',
      '/login?returnUrl=%2Forganizations%2Fregister',
    );
    expect(screen.getByText(/if you already have an account/i)).toHaveTextContent(
      /we emailed you instead with instructions to sign in and add the organization/i,
    );
    expect(screen.getByText(/didn.t get an email/i)).toHaveTextContent(/check your spam folder and wait a few minutes/i);
  });

  it('shows signed-in users only organization fields and submits no credential values', async () => {
    const submissions = [];
    server.use(antiforgery(), contextIs(signedInContext()));
    server.use(http.post('/api/identity/organizations/register', async ({ request }) => {
      submissions.push(await request.json());
      return new HttpResponse(null, { status: 202 });
    }));

    renderPage();
    await screen.findByLabelText('Legal name');
    expect(screen.queryByLabelText('Email')).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Password')).not.toBeInTheDocument();

    await userEvent.type(screen.getByLabelText('Legal name'), 'Northwind SA');
    await userEvent.type(screen.getByLabelText('CUIT'), '30-12345678-1');
    await userEvent.click(screen.getByRole('button', { name: 'Register' }));

    await waitFor(() => expect(submissions).toEqual([{
      legalName: 'Northwind SA',
      cuit: '30-12345678-1',
      email: '',
      password: '',
    }]));
    expect(await screen.findByRole('status')).toHaveTextContent(/we have sent it a confirmation link/i);
    expect(screen.queryByRole('link', { name: 'Sign in' })).not.toBeInTheDocument();
  });

  it('binds and focuses a signed-in organization-field refusal without restoring credential fields', async () => {
    server.use(antiforgery(), contextIs(signedInContext()));
    server.use(http.post('/api/identity/organizations/register', () => problem(400, 'validation_failed', {
      status: 400,
      errors: {
        legalName: [{ code: 'required', params: {} }],
        cuit: [{ code: 'cuit_check_digit', params: {} }],
      },
    })));

    renderPage();
    await screen.findByLabelText('Legal name');
    expect(screen.queryByLabelText('Email')).not.toBeInTheDocument();
    await userEvent.type(screen.getByLabelText('Legal name'), 'Northwind SA');
    await userEvent.type(screen.getByLabelText('CUIT'), '30-12345678-1');
    await userEvent.click(screen.getByRole('button', { name: 'Register' }));

    const cuit = screen.getByLabelText('CUIT');
    await waitFor(() => expect(cuit).toHaveFocus());
    expect(cuit).toHaveAttribute('aria-invalid', 'true');
    expect(cuit).toHaveAccessibleDescription("That CUIT's check digit does not match. Check the number.");
    expect(screen.getByLabelText('Legal name')).toHaveAccessibleDescription('A legal name is required.');
    expect(screen.getByRole('alert')).not.toHaveTextContent("CUIT: That CUIT's check digit does not match. Check the number.");
    expect(screen.queryByLabelText('Email')).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Password')).not.toBeInTheDocument();
    await userEvent.tab();
    expect(screen.getByLabelText('Legal name')).toHaveFocus();
  });
});

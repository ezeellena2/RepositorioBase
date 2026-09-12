/* eslint-disable i18next/no-literal-string -- API contract fixture codes are not UI copy. */
import { act, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { i18n } from '../../i18n';
import { PROBLEM_MEDIA_TYPE, readProblem } from './api/problemDetails';
import { ProblemMessage } from './ProblemMessage';

afterEach(async () => {
  vi.useRealTimers();
  await i18n.changeLanguage('en');
});

describe('ProblemMessage', () => {
  it('omits case-insensitively claimed errors and links rendered field errors without exposing raw keys', () => {
    render(
      <>
        <input id="recovery-code" aria-label="Recovery code" />
        <ProblemMessage
          problem={{
            code: 'validation_failed',
            errors: {
              NewPassword: [{ code: 'too_long', params: { max: 12 } }],
              RecoveryCode: [{ code: 'required', params: {} }],
              request: [{ code: 'invalid', params: {} }],
            },
          }}
          claimedFields={['newPassword']}
          fieldIds={{ recoveryCode: 'recovery-code' }}
        />
      </>,
    );

    const alert = screen.getByRole('alert');
    expect(alert).not.toHaveTextContent('Must be at most 12 characters.');
    expect(alert).not.toHaveTextContent('NewPassword');
    expect(alert).not.toHaveTextContent('RecoveryCode');
    expect(alert).not.toHaveTextContent('request:');
    expect(screen.getByRole('link', { name: 'Recovery code: This value is required.' })).toHaveAttribute(
      'href',
      '#recovery-code',
    );
    expect(alert).toHaveTextContent('Request: This value is not valid.');
  });

  it.each([
    ['invalid_confirmation', 'That confirmation link is not usable.'],
    ['registration_conflict', 'That organization registration cannot be completed. If you already have an account at this address, sign in; otherwise start registration again.'],
    ['invalid_credential_token', 'That reset link has expired or was already used. Ask for a new one.'],
    ['personal_registration_conflict', 'Your personal account could not be created. If this address is confirmed, sign in and try again.'],
    ['session_not_found', 'That session has already ended. Refresh the list.'],
    ['personal_profile_concurrency_conflict', 'Your profile changed while you were editing. Refresh and try again.'],
    ['profile_field_not_editable', 'That detail cannot be changed here.'],
    ['email_confirmation_required', 'Confirm your email address first.'],
    ['invalid_request', 'We could not read that request. Reload the page and try again.'],
  ])('gives %s its exact neutral next step', (code, message) => {
    render(<ProblemMessage problem={{ code }} />);

    expect(screen.getByRole('alert')).toHaveTextContent(message);
  });

  it('keeps an intentional generic fallback for an unreadable code', () => {
    render(<ProblemMessage problem={{ code: 'not-from-this-contract' }} />);

    expect(screen.getByRole('alert')).toHaveTextContent('That request could not be completed.');
  });

  it('shows one selectable opaque reference inside the single alert for a server 500', () => {
    render(<ProblemMessage problem={{ status: 500, code: 'internal_server_error', traceId: 'trace-500' }} />);

    const alert = screen.getByRole('alert');
    expect(screen.getAllByRole('alert')).toHaveLength(1);
    expect(alert).toHaveTextContent('Something went wrong. Try again.');
    expect(alert).toHaveTextContent('Reference: trace-500');
    expect(alert.querySelectorAll('p')).toHaveLength(2);
  });

  it('does not show a support reference for a 4xx refusal', () => {
    render(<ProblemMessage problem={{ status: 403, code: 'permission_denied', traceId: 'trace-403' }} />);

    expect(screen.getByRole('alert')).not.toHaveTextContent('Reference:');
  });

  it('focuses and scrolls an opted-in alert without relying on the browser scroll side effect', () => {
    const scrollIntoView = vi.fn();
    const previous = HTMLElement.prototype.scrollIntoView;
    HTMLElement.prototype.scrollIntoView = scrollIntoView;

    try {
      render(<ProblemMessage problem={{ status: 409, code: 'invitation_conflict' }} autoFocus />);

      const alert = screen.getByRole('alert');
      expect(alert).toHaveAttribute('tabindex', '-1');
      expect(alert).toHaveFocus();
      expect(scrollIntoView).toHaveBeenCalledWith({ block: 'nearest', behavior: 'auto' });
    } finally {
      HTMLElement.prototype.scrollIntoView = previous;
    }
  });

  it('keeps an ordinary alert programmatically focusable without stealing focus', () => {
    const before = document.createElement('button');
    document.body.append(before);
    before.focus();

    render(<ProblemMessage problem={{ status: 403, code: 'permission_denied' }} />);

    expect(screen.getByRole('alert')).toHaveAttribute('tabindex', '-1');
    expect(before).toHaveFocus();
  });

  it('counts a stable Retry-After problem down without refocusing and hides the wait at zero', () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2026-09-11T12:00:00Z'));
    const problem = { status: 429, code: 'rate_limit_exceeded', retryAfterSeconds: 2 };
    const focus = vi.spyOn(HTMLElement.prototype, 'focus');

    try {
      render(<ProblemMessage problem={problem} autoFocus />);

      expect(screen.getByRole('alert')).toHaveTextContent('Try again in 2 seconds.');
      expect(focus).toHaveBeenCalledTimes(1);

      act(() => { vi.advanceTimersByTime(1_000); });
      expect(screen.getByRole('alert')).toHaveTextContent('Try again in 1 seconds.');
      expect(focus).toHaveBeenCalledTimes(1);

      act(() => { vi.advanceTimersByTime(1_000); });
      expect(screen.getByRole('alert')).not.toHaveTextContent(/Try again in/);
      expect(focus).toHaveBeenCalledTimes(1);
    } finally {
      focus.mockRestore();
    }
  });
});

describe('ProblemMessage validation details', () => {
  it.each([
    ['en', 'Email: Must be at most 256 characters.'],
    ['es', 'Correo electrónico: Debe tener como máximo 256 caracteres.'],
  ])('renders the translated field label and interpolated validation code in %s', async (language, expected) => {
    await i18n.changeLanguage(language);
    render(<ProblemMessage problem={{
      code: 'validation_failed',
      status: 400,
      errors: { email: [{ code: 'too_long', params: { max: 256 } }] },
    }} />);

    expect(screen.getByText(expected)).toBeInTheDocument();
  });

  it.each([
    ['en', 'Email: This value is not valid.'],
    ['es', 'Correo electrónico: Este valor no es válido.'],
  ])('uses the localized fallback in %s without exposing an adversarial code or params', async (language, expected) => {
    await i18n.changeLanguage(language);
    const problem = await readProblem(new Response(JSON.stringify({
      code: 'validation_failed',
      errors: { email: [{ code: 'fields.email', params: { lngs: 8675309, PropertyValue: 42 } }] },
    }), {
      status: 400,
      headers: { 'Content-Type': PROBLEM_MEDIA_TYPE },
    }));

    render(<ProblemMessage problem={problem} />);

    expect(screen.getByText(expected)).toBeInTheDocument();
    expect(document.body).not.toHaveTextContent(/fields\.email|lngs|PropertyValue|8675309|42/);
    expect(i18n.resolvedLanguage).toBe(language);
  });
});

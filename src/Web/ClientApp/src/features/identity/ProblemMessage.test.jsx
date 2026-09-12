/* eslint-disable i18next/no-literal-string -- API contract fixture codes are not UI copy. */
import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { i18n } from '../../i18n';
import { PROBLEM_MEDIA_TYPE, readProblem } from './api/problemDetails';
import { ProblemMessage } from './ProblemMessage';

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

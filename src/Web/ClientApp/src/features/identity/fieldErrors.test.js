import { describe, expect, it } from 'vitest';
import { validatePersonalRegistration } from './fieldErrors';

const valid = (overrides = {}) => ({
  fullName: 'Ada Lovelace',
  displayName: 'Ada',
  documentNumber: '12.345.678',
  email: 'ada@example.test',
  password: 'Testing1234!',
  ...overrides,
});

describe('identity client validation details', () => {
  it('classifies email shape without claiming more than the server predicate establishes', () => {
    expect(validatePersonalRegistration(valid({ email: 'not-an-address' }))).toEqual({
      email: [{ code: 'email_format', params: {} }],
    });
  });

  it.each([
    ['12/345.678', 'dni_characters'],
    ['12.345', 'dni_length'],
  ])('classifies the correctable DNI rule for %s', (documentNumber, code) => {
    expect(validatePersonalRegistration(valid({ documentNumber }))).toEqual({
      documentNumber: [{ code, params: {} }],
    });
  });

  it('preserves separator and leading-zero normalization accepted by the backend', () => {
    expect(validatePersonalRegistration(valid({ documentNumber: ' 07.123.456 ' }))).toEqual({});
  });
});

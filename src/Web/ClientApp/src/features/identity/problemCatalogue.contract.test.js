import { describe, expect, it } from 'vitest';
import { problem } from '../../test/identityServer';
import problemCodes from './problemCodes.json';
import { CLIENT_CODES, MESSAGES } from './problemMessages';

describe('problem catalogue contract', () => {
  it('keeps server codes server-only and makes the UI catalogue their union with four client codes', () => {
    expect(problemCodes.recovery_admission_closed).toBe(503);
    expect(MESSAGES.recovery_admission_closed).toBe(
      'The service is not accepting requests right now. Try again shortly.',
    );
    expect(MESSAGES.invalid_role_operation).toBe('That role change was not accepted.');
    expect(MESSAGES.invalid_membership_operation).toBe('That membership change was not accepted.');
    expect(MESSAGES.invalid_confirmation).toBe('That confirmation link is not usable.');
    expect(MESSAGES.registration_conflict).toBe(
      'That organization registration cannot be completed. If you already have an account at this address, sign in; otherwise start registration again.',
    );
    expect(Object.keys(problemCodes)).toHaveLength(53);
    expect(CLIENT_CODES).toEqual([
      'network_unavailable',
      'request_timeout',
      'unreadable_response',
      'client_failure',
    ]);
    expect(CLIENT_CODES.every((code) => !(code in problemCodes))).toBe(true);
    expect(Object.keys(MESSAGES).sort()).toEqual([...Object.keys(problemCodes), ...CLIENT_CODES].sort());
    expect(Object.keys(MESSAGES)).toHaveLength(57);
    expect(Object.values(MESSAGES).every((message) => message.trim().length > 0)).toBe(true);
  });

  it('allows MSW fixtures only for status and code pairs declared by the API', () => {
    expect(() => problem(400, 'validation_failed')).not.toThrow();
    expect(() => problem(409, 'validation_failed')).toThrow(
      'Problem fixture 409 validation_failed is not declared in problemCodes.json.',
    );
    expect(() => problem(400, 'not_a_problem_code')).toThrow(
      'Problem fixture 400 not_a_problem_code is not declared in problemCodes.json.',
    );
  });
});

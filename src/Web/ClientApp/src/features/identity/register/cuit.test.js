import { describe, expect, it } from 'vitest';
import { cuitError } from './cuit';

describe('CUIT input validation', () => {
  it.each([
    ['30-12345678-1', null],
    ['20-12345678-6', null],
    ['99-12345678-1', null],
    ['30-12345678-9', { code: 'invalid', params: {} }],
    ['20-00000001-0', { code: 'invalid', params: {} }],
  ])('applies the same check-digit rule as the domain for %s', (value, expected) => {
    expect(cuitError(value)).toEqual(expected);
  });

  it.each([
    ['', { code: 'required', params: {} }],
    ['1'.repeat(33), { code: 'too_long', params: { max: 32 } }],
    ['30.12345678.1', { code: 'invalid', params: {} }],
  ])('returns a structured client-owned detail for %s', (value, expected) => {
    expect(cuitError(value)).toEqual(expected);
  });
});

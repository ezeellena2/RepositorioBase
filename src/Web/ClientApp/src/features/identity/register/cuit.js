const WEIGHTS = [5, 4, 3, 2, 7, 6, 5, 4, 3, 2];

/** Mirrors NormalizedCuit.Evaluate for immediate feedback; the server always validates again. */
export function cuitError(value) {
  const raw = value ?? '';
  if (!raw.trim()) return { code: 'required', params: {} };
  if (raw.length > 32) return { code: 'too_long', params: { max: 32 } };

  const digits = raw.replace(/\D/g, '');
  if (!/^[0-9\s-]+$/.test(raw) || digits.length !== 11) {
    return { code: 'invalid', params: {} };
  }

  const sum = WEIGHTS.reduce(
    (total, weight, index) => total + weight * Number(digits[index]),
    0,
  );
  const expected = (11 - (sum % 11)) % 11;
  return expected === 10 || expected !== Number(digits[10])
    ? { code: 'invalid', params: {} }
    : null;
}

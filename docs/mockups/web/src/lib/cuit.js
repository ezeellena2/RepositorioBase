const PERSONA = ['20', '23', '24', '25', '26', '27'];
const EMPRESA = ['30', '33', '34'];

export function normalize(input) {
  const digits = String(input ?? '').replace(/\D/g, '');
  return digits.length === 11 ? digits : null;
}

export function kind(normalized) {
  if (!normalized) return null;
  const prefix = normalized.slice(0, 2);
  if (PERSONA.includes(prefix)) return 'persona';
  if (EMPRESA.includes(prefix)) return 'empresa';
  return null;
}

export function isValid(input) {
  const n = normalize(input);
  if (!n || !kind(n)) return false;
  const weights = [5, 4, 3, 2, 7, 6, 5, 4, 3, 2];
  let sum = 0;
  for (let i = 0; i < 10; i++) sum += Number(n[i]) * weights[i];
  let check = 11 - (sum % 11);
  if (check === 11) check = 0;
  if (check === 10) check = 9;
  return check === Number(n[10]);
}

export function format(normalized) {
  if (!normalized || normalized.length !== 11) return normalized ?? '';
  return `${normalized.slice(0, 2)}-${normalized.slice(2, 10)}-${normalized.slice(10)}`;
}

import { VALIDATION_CODE_SCHEMA } from './api/problemDetails';

/** A bounded server validation detail rendered at its owning input, never as provider prose. */
export function fieldError(problem, name, t) {
  const details = problem?.errors?.[name];
  if (!details?.length) return { error: false, helperText: undefined };
  const helperText = details.map((detail) => {
    const key = Object.hasOwn(VALIDATION_CODE_SCHEMA, detail.code)
      ? `errors:validation.${detail.code}`
      : 'errors:validation.unknown';
    return t(key, { replace: detail.params });
  }).join(' ');
  return { error: true, helperText };
}

export const firstInvalid = (problem, names) => names.find((name) => problem?.errors?.[name]?.length > 0);

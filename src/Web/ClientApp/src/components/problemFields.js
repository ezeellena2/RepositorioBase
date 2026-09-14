import { VALIDATION_CODE_SCHEMA } from '../api/problemDetails';

const REQUIRED_FIELD_KEYS = Object.freeze({
  legalname: 'legalName',
  cuit: 'cuit',
  email: 'email',
  password: 'password',
  fullname: 'fullName',
  displayname: 'displayName',
  documentnumber: 'documentNumber',
  roleids: 'roleIds',
  name: 'name',
  newpassword: 'newPassword',
  token: 'token',
  confirmationtoken: 'confirmationToken',
  code: 'code',
  recoverycode: 'recoveryCode',
  language: 'language',
  version: 'version',
});

export const validationDetailText = (error, t, field) => {
  const requiredField = typeof field === 'string'
    ? REQUIRED_FIELD_KEYS[field.toLowerCase()]
    : undefined;
  if (error?.code === 'required' && requiredField) {
    return t(`errors:validation.requiredFields.${requiredField}`);
  }

  const key = Object.hasOwn(VALIDATION_CODE_SCHEMA, error?.code)
    ? `errors:validation.${error.code}`
    : 'errors:validation.unknown';
  return t(key, { replace: error?.params ?? {} });
};

const sameFieldName = (left, right) => (
  typeof left === 'string'
  && typeof right === 'string'
  && left.toLowerCase() === right.toLowerCase()
);

const detailsForField = (available, field) => Object.entries(available ?? {})
  .filter(([candidate]) => sameFieldName(candidate, field))
  .flatMap(([, details]) => Array.isArray(details)
    ? details.filter((item) => item && typeof item === 'object' && typeof item.code === 'string')
    : []);

export function selectFieldErrors(problem, fields) {
  const available = problem?.errors ?? {};
  return fields.reduce((selected, field) => {
    const details = detailsForField(available, field);
    return details.length > 0
      ? { ...selected, [field]: details }
      : selected;
  }, {});
}

export function claimedFieldNames(problem, fields) {
  const available = problem?.errors ?? {};
  return fields.filter((field) => detailsForField(available, field).length > 0);
}

export function unclaimedFieldErrors(problem, claimedFields) {
  return Object.entries(problem?.errors ?? {})
    .filter(([field]) => !claimedFields.some((claimed) => sameFieldName(field, claimed)));
}

export function fieldIdFor(fieldIds, serverField) {
  return Object.entries(fieldIds ?? {})
    .find(([field]) => sameFieldName(field, serverField))?.[1];
}

export function clearFieldError(errors, field) {
  const matching = Object.keys(errors ?? {}).filter((candidate) => sameFieldName(candidate, field));
  if (matching.length === 0) return errors;
  const next = { ...errors };
  matching.forEach((candidate) => delete next[candidate]);
  return next;
}

/** A bounded validation detail rendered at its owning input, never as provider prose. */
export function fieldError(problem, name, t) {
  const details = detailsForField(problem?.errors, name);
  return {
    error: details.length > 0,
    helperText: details.length > 0
      ? details.map((item) => validationDetailText(item, t, name)).join(' ')
      : undefined,
  };
}

export const firstInvalid = (problem, names) => (
  names.find((name) => detailsForField(problem?.errors, name).length > 0)
);

export const fieldErrorText = (errors, field, t) => (
  detailsForField(errors, field).map((item) => validationDetailText(item, t, field)).join(' ')
);

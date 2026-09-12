import { VALIDATION_CODE_SCHEMA } from './api/problemDetails';
import { cuitError } from './register/cuit';

const detail = (code, params = {}) => ({ code, params });
const addError = (errors, field, error) => ({ ...errors, [field]: [error] });

const translateDetail = (error, t) => {
  const key = Object.hasOwn(VALIDATION_CODE_SCHEMA, error?.code)
    ? `errors:validation.${error.code}`
    : 'errors:validation.unknown';
  return t(key, { replace: error?.params ?? {} });
};

const validateEmail = (errors, email) => {
  if (!email?.trim()) return addError(errors, 'email', detail('required'));
  if (email.length > 256) return addError(errors, 'email', detail('too_long', { max: 256 }));
  if (!email.trim().includes('@')) return addError(errors, 'email', detail('invalid'));
  return errors;
};

const validatePasswordShape = (errors, password) => {
  if (!password?.trim()) return addError(errors, 'password', detail('required'));
  if (password.length > 256) return addError(errors, 'password', detail('too_long', { max: 256 }));
  return errors;
};

const validateName = (errors, field, value, maximumLength) => {
  if (!value?.trim()) return addError(errors, field, detail('required'));
  if (value.length > maximumLength) {
    return addError(errors, field, detail('too_long', { max: maximumLength }));
  }
  return errors;
};

const validateCuit = (errors, cuit) => {
  const error = cuitError(cuit);
  return error ? addError(errors, 'cuit', error) : errors;
};

const validateDocument = (errors, documentNumber) => {
  if (!documentNumber?.trim()) return addError(errors, 'documentNumber', detail('required'));
  if (documentNumber.length > 32) {
    return addError(errors, 'documentNumber', detail('too_long', { max: 32 }));
  }
  const digits = [...documentNumber]
    .filter((character) => /[0-9]/.test(character))
    .join('')
    .replace(/^0+/, '');
  if (!/^[0-9.\s-]+$/.test(documentNumber) || digits.length < 7 || digits.length > 8) {
    return addError(errors, 'documentNumber', detail('invalid'));
  }
  return errors;
};

export const organizationRegistrationFields = ['legalName', 'cuit', 'email', 'password'];
export const personalRegistrationFields = ['fullName', 'displayName', 'documentNumber', 'email', 'password'];

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

export function validateOrganizationRegistration(form, includeCredentials = true) {
  let errors = {};
  errors = validateName(errors, 'legalName', form.legalName, 256);
  errors = validateCuit(errors, form.cuit);
  if (!includeCredentials) return errors;
  errors = validateEmail(errors, form.email);
  return validatePasswordShape(errors, form.password);
}

export function validatePersonalRegistration(form) {
  let errors = {};
  errors = validateName(errors, 'fullName', form.fullName, 200);
  errors = validateName(errors, 'displayName', form.displayName, 60);
  errors = validateDocument(errors, form.documentNumber);
  errors = validateEmail(errors, form.email);
  return validatePasswordShape(errors, form.password);
}

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
    helperText: details.length > 0 ? details.map((item) => translateDetail(item, t)).join(' ') : undefined,
  };
}

export const firstInvalid = (problem, names) => (
  names.find((name) => detailsForField(problem?.errors, name).length > 0)
);

export const fieldErrorText = (errors, field, t) => (
  detailsForField(errors, field).map((item) => translateDetail(item, t)).join(' ')
);

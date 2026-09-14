import { cuitError } from './register/cuit';

const detail = (code, params = {}) => ({ code, params });
const addError = (errors, field, error) => ({ ...errors, [field]: [error] });

const validateEmail = (errors, email) => {
  if (!email?.trim()) return addError(errors, 'email', detail('required'));
  if (email.length > 256) return addError(errors, 'email', detail('too_long', { max: 256 }));
  if (!email.trim().includes('@')) return addError(errors, 'email', detail('email_format'));
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
  if (!/^[0-9.\s-]+$/.test(documentNumber)) {
    return addError(errors, 'documentNumber', detail('dni_characters'));
  }
  if (digits.length < 7 || digits.length > 8) {
    return addError(errors, 'documentNumber', detail('dni_length'));
  }
  return errors;
};

export const organizationRegistrationFields = ['legalName', 'cuit', 'email', 'password'];
export const personalRegistrationFields = ['fullName', 'displayName', 'documentNumber', 'email', 'password'];

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

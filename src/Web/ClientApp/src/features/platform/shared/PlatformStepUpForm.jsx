import { useEffect, useRef } from 'react';
import Button from '@mui/material/Button';
import Stack from '@mui/material/Stack';
import TextField from '@mui/material/TextField';
import { useTranslation } from '../../../i18n';
import { fieldError } from '../../identity/fieldErrors';

const form = { maxWidth: 360 };
const stepUpFieldSlots = { inputLabel: { required: false }, htmlInput: { inputMode: 'numeric' } };

/**
 * The one form every Platform screen asks the second factor with. It is fully controlled and owns no state: the
 * code lives with whoever renders it, so it survives a screen going from gated to loaded instead of being dropped
 * when this component unmounts.
 *
 * `inputId` is a parameter because two of these can be on one page and an id may not repeat; the label points at
 * whichever one it belongs to.
 *
 * `submitVariant` is a parameter for the same reason and defaults to the gate's own weight. Where the ceremony is
 * the whole screen it is the primary action and stays `contained`; where it sits above a directory the screen's
 * primary action is elsewhere, and a second filled button would claim an emphasis this form does not have.
 */
export function PlatformStepUpForm({ inputId, code, onCodeChange, onSubmit, isBusy, problem, submitVariant = 'contained' }) {
  const { t } = useTranslation('platform');
  const codeInput = useRef(null);
  const structured = fieldError(problem, 'code', t);
  const invalidMfaCode = problem?.code === 'invalid_mfa_code';
  const invalid = structured.error || invalidMfaCode;

  useEffect(() => {
    if (invalid) codeInput.current?.focus();
  }, [invalid]);

  return (
    <Stack
      component="form"
      spacing={2}
      aria-label={t('stepUp.label')}
      sx={form}
      onSubmit={(event) => { event.preventDefault(); onSubmit(); }}
    >
      <TextField
        id={inputId}
        inputRef={codeInput}
        label={t('stepUp.authenticatorCode')}
        type="text"
        required
        fullWidth
        slotProps={stepUpFieldSlots}
        error={invalid}
        helperText={invalidMfaCode ? t('errors:invalid_mfa_code') : structured.helperText}
        value={code}
        onChange={(event) => onCodeChange(event.target.value)}
      />
      <Button type="submit" variant={submitVariant} disabled={isBusy} sx={{ alignSelf: 'flex-start' }}>{t('stepUp.label')}</Button>
    </Stack>
  );
}

/* eslint-disable i18next/no-literal-string -- bounded validation protocol keys, not display copy. */
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Typography from '@mui/material/Typography';
import { useEffect, useRef } from 'react';
import { useTranslation } from '../../i18n';
import { VALIDATION_CODE_SCHEMA } from './api/problemDetails';

/**
 * Shows what the API said and nothing more. The stable code decides the message, so the wording is ours and the
 * server's diagnostics stay where they belong; field errors are shown only where the API indexed them.
 */
export function ProblemMessage({ problem, claimed = [], autoFocus = false }) {
  const { t, i18n } = useTranslation('errors');
  const alertRef = useRef(null);

  useEffect(function focusProblemMessage() {
    if (problem && autoFocus) alertRef.current?.focus();
  }, [problem, autoFocus]);

  if (!problem) return null;

  const fields = Object.entries(problem.errors ?? {}).filter(([field]) => !claimed.includes(field));
  const messageKey = problem.code && i18n.exists(`errors:${problem.code}`)
    ? `errors:${problem.code}`
    : 'errors:unknown';

  return (
    <Alert ref={alertRef} severity="error" tabIndex={autoFocus ? -1 : undefined}>
      <Typography variant="body2">{t(messageKey)}</Typography>
      {problem.retryAfterSeconds !== undefined && (
        <Typography variant="body2">{t('errors:retryAfter', { seconds: problem.retryAfterSeconds })}</Typography>
      )}
      {fields.length > 0 && (
        <Box component="ul" sx={{ m: 0, pl: 2.5 }}>
          {fields.flatMap(([field, details]) => details.map((detail, index) => {
            const fieldKey = `errors:validation.fields.${field}`;
            const messageKey = Object.hasOwn(VALIDATION_CODE_SCHEMA, detail.code)
              && i18n.exists(`errors:validation.${detail.code}`)
              ? `errors:validation.${detail.code}`
              : 'errors:validation.unknown';
            return (
              <li key={`${field}-${index}`}>
                {t('errors:validation.fieldMessage', {
                  field: i18n.exists(fieldKey) ? t(fieldKey) : t('errors:validation.fields.unknown'),
                  message: t(messageKey, { replace: detail.params }),
                })}
              </li>
            );
          }))}
        </Box>
      )}
    </Alert>
  );
}

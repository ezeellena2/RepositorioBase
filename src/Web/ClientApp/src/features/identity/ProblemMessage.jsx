/* eslint-disable i18next/no-literal-string -- bounded validation protocol keys, not display copy. */
import { useEffect, useRef, useState } from 'react';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Link from '@mui/material/Link';
import Typography from '@mui/material/Typography';
import { useTranslation } from '../../i18n';
import { VALIDATION_CODE_SCHEMA } from './api/problemDetails';
import { fieldIdFor, unclaimedFieldErrors } from './fieldErrors';

const validationFieldKey = (i18n, field) => {
  const catalog = i18n.getResource(i18n.resolvedLanguage, 'errors', 'validation.fields');
  const canonical = Object.keys(catalog ?? {})
    .find((candidate) => candidate.toLowerCase() === field.toLowerCase());
  return canonical ? `errors:validation.fields.${canonical}` : 'errors:validation.fields.unknown';
};

/**
 * Shows what the API said and nothing more. The stable code decides the message, so the wording is ours and the
 * server's diagnostics stay where they belong; field errors are shown only where the API indexed them.
 */
export function ProblemMessage({ problem, claimedFields = [], fieldIds = {}, autoFocus = false }) {
  const { t, i18n } = useTranslation('errors');
  const alertRef = useRef(null);
  const retryAfterSeconds = Number(problem?.retryAfterSeconds);
  const initialWait = Number.isFinite(retryAfterSeconds) && retryAfterSeconds > 0
    ? Math.ceil(retryAfterSeconds)
    : null;
  const [countdown, setCountdown] = useState(null);
  const remainingWait = countdown?.problem === problem ? countdown.seconds : initialWait;

  useEffect(() => {
    if (initialWait === null) return undefined;

    const deadline = Date.now() + retryAfterSeconds * 1_000;
    const timer = setInterval(() => {
      const seconds = Math.max(0, Math.ceil((deadline - Date.now()) / 1_000));
      setCountdown({ problem, seconds });
      if (seconds === 0) clearInterval(timer);
    }, 1_000);
    return () => clearInterval(timer);
  }, [initialWait, problem, retryAfterSeconds]);

  useEffect(() => {
    if (!problem || !autoFocus) return;

    const alert = alertRef.current;
    if (!alert) return;

    alert.focus({ preventScroll: true });
    const prefersReducedMotion = typeof window.matchMedia !== 'function'
      || window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    alert.scrollIntoView?.({
      block: 'nearest',
      behavior: prefersReducedMotion ? 'auto' : 'smooth',
    });
  }, [autoFocus, problem]);

  if (!problem) return null;

  const fields = unclaimedFieldErrors(problem, claimedFields);
  const problemMessageKey = problem.code && i18n.exists(`errors:${problem.code}`)
    ? `errors:${problem.code}`
    : 'errors:unknown';

  return (
    <Alert ref={alertRef} severity="error" role="alert" tabIndex={-1}>
      <Typography variant="body2">{t(problemMessageKey)}</Typography>
      {remainingWait > 0 && (
        <Typography variant="body2">{t('errors:retryAfter', { seconds: remainingWait })}</Typography>
      )}
      {problem.status >= 500 && problem.traceId && (
        <Typography component="p" variant="caption">
          {t('errors:reference', { traceId: problem.traceId })}
        </Typography>
      )}
      {fields.length > 0 && (
        <Box component="ul" sx={{ m: 0, pl: 2.5 }}>
          {fields.flatMap(([field, details]) => details.map((detail, index) => {
            const fieldKey = validationFieldKey(i18n, field);
            const detailKey = Object.hasOwn(VALIDATION_CODE_SCHEMA, detail.code)
              && i18n.exists(`errors:validation.${detail.code}`)
              ? `errors:validation.${detail.code}`
              : 'errors:validation.unknown';
            const message = t('errors:validation.fieldMessage', {
              field: t(fieldKey),
              message: t(detailKey, { replace: detail.params }),
            });
            const fieldId = fieldIdFor(fieldIds, field);
            return (
              <li key={`${field}-${index}`}>
                {fieldId ? <Link href={`#${fieldId}`}>{message}</Link> : message}
              </li>
            );
          }))}
        </Box>
      )}
    </Alert>
  );
}

import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Typography from '@mui/material/Typography';
import { useTranslation } from '../../i18n';

/**
 * Shows what the API said and nothing more. The stable code decides the message, so the wording is ours and the
 * server's diagnostics stay where they belong; field errors are shown only where the API indexed them.
 */
export function ProblemMessage({ problem }) {
  const { t, i18n } = useTranslation('errors');

  if (!problem) return null;

  const fields = Object.entries(problem.errors ?? {});
  const messageKey = problem.code && i18n.exists(`errors:${problem.code}`)
    ? `errors:${problem.code}`
    : 'errors:unknown';

  return (
    <Alert severity="error">
      <Typography variant="body2">{t(messageKey)}</Typography>
      {problem.retryAfterSeconds !== undefined && (
        <Typography variant="body2">{t('errors:retryAfter', { seconds: problem.retryAfterSeconds })}</Typography>
      )}
      {fields.length > 0 && (
        <Box component="ul" sx={{ m: 0, pl: 2.5 }}>
          {fields.map(([field, messages]) => <li key={field}>{t('errors:fieldMessage', { field, messages: messages.join(' ') })}</li>)}
        </Box>
      )}
    </Alert>
  );
}

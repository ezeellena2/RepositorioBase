/**
 * Shows what the API said and nothing more. The stable code decides the message, so the wording is ours and the
 * server's diagnostics stay where they belong; field errors are shown only where the API indexed them.
 */
const MESSAGES = {
  antiforgery_validation_failed: 'Your session moved on. Try that again.',
  authentication_required: 'Sign in to continue.',
  invalid_session: 'Your session is no longer valid. Sign in again.',
  permission_denied: 'You do not have permission to do that here.',
  not_found: 'That is not available.',
  invalid_registration: 'Check the details and try again.',
  invalid_confirmation: 'That confirmation link is not usable.',
  invalid_invitation: 'That invitation is not usable.',
  invitation_conflict: 'That invitation cannot be completed in its current state.',
  registration_conflict: 'That organization cannot be registered right now.',
  session_concurrency_conflict: 'Something changed while you were working. Try again.',
  rate_limit_exceeded: 'Too many attempts. Wait a moment and try again.',
  internal_server_error: 'Something went wrong. Try again.',
};

export function ProblemMessage({ problem }) {
  if (!problem) return null;

  const fields = Object.entries(problem.errors ?? {});
  return (
    <div role="alert">
      <p>{MESSAGES[problem.code] ?? 'That request could not be completed.'}</p>
      {problem.retryAfterSeconds !== undefined && <p>Try again in {problem.retryAfterSeconds} seconds.</p>}
      {fields.length > 0 && (
        <ul>
          {fields.map(([field, messages]) => <li key={field}>{field}: {messages.join(' ')}</li>)}
        </ul>
      )}
    </div>
  );
}

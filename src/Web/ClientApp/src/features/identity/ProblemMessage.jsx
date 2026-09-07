/**
 * Shows what the API said and nothing more. The stable code decides the message, so the wording is ours and the
 * server's diagnostics stay where they belong; field errors are shown only where the API indexed them.
 */
const MESSAGES = {
  antiforgery_validation_failed: 'Your session moved on. Try that again.',
  authentication_required: 'Sign in to continue.',
  invalid_session: 'Your session is no longer valid. Sign in again.',
  credential_superseded: 'Your password changed while you were signing in. Sign in again with the new one.',
  permission_denied: 'You do not have permission to do that here.',
  not_found: 'That is not available.',
  invalid_registration: 'Check the details and try again.',
  invalid_confirmation: 'That confirmation link is not usable.',
  invalid_invitation: 'That invitation is not usable.',
  invitation_conflict: 'That invitation cannot be completed in its current state.',
  registration_conflict: 'That organization cannot be registered right now.',
  session_concurrency_conflict: 'Something changed while you were working. Try again.',
  platform_tenant_concurrency_conflict: 'That tenant changed while you were working. Refresh it and try again.',
  invalid_platform_operation: 'That Platform operation is not valid in its current state.',
  recent_mfa_required: 'Confirm your second factor again before making this change.',
  recent_proof_required: 'Confirm your password again before making this change.',
  invalid_credential_proof: 'That password was not accepted. Try again.',
  invalid_external_login: 'That sign-in with a provider could not be completed. Try again.',
  external_login_conflict: 'That provider account cannot be used here. Sign in and link it from your account.',
  provider_already_linked: 'That provider is already linked to this account.',
  last_authenticator_required: 'You cannot remove your only way to sign in. Add another one first.',
  invalid_role_operation: 'That role change is not allowed. You can only grant permissions you hold yourself.',
  invalid_membership_operation: 'That membership change is not allowed right now.',
  role_concurrency_conflict: 'That role changed while you were editing it. Refresh and try again.',
  membership_concurrency_conflict: 'That member changed while you were editing. Refresh and try again.',
  last_administrator_required: 'This would leave the organization with no administrator. Give somebody else those permissions first.',
  owner_required: 'Only the current owner can do that.',
  invalid_reactivation: 'That reactivation link is not usable, or the password did not match. Ask for a new link and try again.',
  platform_last_owner: 'This would leave the Platform with no owner. Somebody else has to hold it first.',
  identity_concurrency_conflict: 'Your account changed while you were working. Refresh and try again.',
  identity_reactivation_unavailable: 'That account cannot be reactivated.',
  platform_mfa_concurrency_conflict: 'Your second factor changed while you were working. Start again.',
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

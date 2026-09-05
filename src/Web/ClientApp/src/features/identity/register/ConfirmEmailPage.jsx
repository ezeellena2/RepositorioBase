import { Link } from 'react-router-dom';
import { useIdentity } from '../context/IdentityProvider';
import { ProblemMessage } from '../ProblemMessage';
import { useFragmentToken } from '../useFragmentToken';
import { useSubmit } from '../useSubmit';

/**
 * The screen the confirmation mail opens (IA-REQ-005).
 *
 * Both confirmation messages the system sends — the one a registered organization gets and the one an invited
 * member gets — carry their token to this same link, because both consume the same endpoint. Without this screen
 * the delivered link opened the application shell and nothing consumed the token, so an address could be
 * confirmed only by writing to the database.
 *
 * It is one button rather than a field to paste into: the recipient followed a link out of their own mailbox, and
 * asking them to transcribe a code from it would be friction that proves nothing extra.
 */
export function ConfirmEmailPage() {
  const identity = useIdentity();
  const token = useFragmentToken();
  const { submit, problem, isBusy, result } = useSubmit((secret) => identity.client.confirmEmail(secret));

  return (
    <section aria-labelledby="confirm-email-heading">
      <h1 id="confirm-email-heading">Confirm your email</h1>
      <ProblemMessage problem={problem} />
      {result ? (
        <>
          <p role="status">Your address is confirmed. Sign in to continue.</p>
          <Link to="/login">Sign in</Link>
        </>
      ) : (
        <button type="button" disabled={isBusy || !token} onClick={() => submit(token ?? '')}>
          Confirm my address
        </button>
      )}
      {!token && <p>Open the link from the confirmation email; this page needs the token it carries.</p>}
    </section>
  );
}

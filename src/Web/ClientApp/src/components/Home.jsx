import { Navigate } from 'react-router-dom';
import { useIdentity } from '../features/identity/context/IdentityProvider';

/**
 * The front door, and only that.
 *
 * What stood here was the template's own tour — what the project is built with, and how to run the dev server —
 * which is documentation addressed to whoever is building the product rather than to whoever opens it. A visitor
 * arriving at the front door came to sign in or to register, and both of those are in the bar above; anything
 * else printed here is something they have to read past first. So a visitor is shown nothing rather than a
 * placeholder, because a placeholder is a claim that something is coming and this is a blank page until the
 * product has a front page to put on it.
 *
 * Somebody holding a session is a different visitor asking a different question. Signing in returns them to
 * where they were going, and where they were going is `/` whenever nothing else was asked for — so the blank
 * front door was the first thing the product showed them after letting them in. There is no front page to send
 * them to yet, so they are sent to the one screen that always has something true to say about the session they
 * just opened: what it grants, and in which organization.
 */
export function Home() {
  const identity = useIdentity();

  // Nothing is decided while the context is still being read. Redirecting on a session that has not resolved
  // would bounce somebody off the front door and back, and answering with the blank page would be answering a
  // question nobody has asked yet.
  if (identity === null || identity === undefined || identity.isLoading) return null;

  return identity.isAuthenticated ? <Navigate to="/identity" replace /> : null;
}

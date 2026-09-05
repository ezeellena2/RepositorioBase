import { Navigate, useLocation } from 'react-router-dom';
import { useIdentity } from '../../features/identity/context/IdentityProvider';

/**
 * Sends an unauthenticated visitor to sign in and remembers where they were going. The return URL is rebuilt
 * from the location rather than taken from the query string, so a crafted link cannot use this component to
 * bounce someone to another origin after they authenticate.
 */
export function ProtectedRoute({ children }) {
  const identity = useIdentity();
  const location = useLocation();

  if (!identity || identity.isLoading) return null;
  if (!identity.isAuthenticated) {
    const returnUrl = `${location.pathname}${location.search}`;
    return <Navigate to={`/login?returnUrl=${encodeURIComponent(returnUrl)}`} replace />;
  }

  return children;
}

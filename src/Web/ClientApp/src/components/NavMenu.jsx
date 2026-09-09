import { Link, useNavigate } from 'react-router-dom';
import { useIdentity } from '../features/identity/context/IdentityProvider';
import { ThemeToggle } from './ThemeToggle';

/**
 * What the navigation offers follows the session, and the permissions only decide what is worth showing. Every
 * action behind these links is authorized again by the server, so hiding one is a courtesy to the user rather
 * than a control (SPEC section 7).
 */
function IdentityLinks() {
  const identity = useIdentity();
  const navigate = useNavigate();

  if (!identity || identity.isLoading) return null;

  const handleSignOut = async (event) => {
    event.preventDefault();
    await identity.signOut();
    navigate('/login');
  };

  if (!identity.isAuthenticated) {
    return (
      <>
        <li><Link to="/login">Log in</Link></li>
        <li><Link to="/register">Register</Link></li>
      </>
    );
  }

  const permissions = identity.context?.permissions ?? [];
  return (
    <>
      <li><Link to="/identity">Your access</Link></li>
      <li><Link to="/identity/profile">Your profile</Link></li>
      <li><Link to="/identity/account">Your account</Link></li>
      <li><Link to="/identity/sessions">Your devices</Link></li>
      <li><Link to="/identity/password">Your password</Link></li>
      <li><Link to="/identity/external">Sign-in providers</Link></li>
      <li><Link to="/organizations/select">Organizations</Link></li>
      {permissions.includes('roles.read') && <li><Link to="/roles">Roles</Link></li>}
      {permissions.includes('members.read') && <li><Link to="/members">Members</Link></li>}
      {permissions.includes('members.invite') && <li><Link to="/members/invite">Invite a member</Link></li>}
      {/* Offered only to a session already operating as Platform. It is a convenience, not a control: the
          panel and the API both reauthorize regardless of what the navigation shows. */}
      {permissions.includes('platform.organizations.read') && <li><Link to="/platform">Platform</Link></li>}
      {permissions.includes('platform.identities.read') && <li><Link to="/platform/identities">Platform identities</Link></li>}
      {permissions.includes('platform.retention.read') && <li><Link to="/platform/retention">Retention</Link></li>}
      <li><a href="/login" onClick={handleSignOut}>Log out</a></li>
    </>
  );
}

export function NavMenu() {
  return (
    <header>
      <nav>
        <ul>
          <li><Link to="/">Clean Architecture</Link></li>
        </ul>
        <ul>
          <li><Link to="/">Home</Link></li>
          <li><Link to="/counter">Counter</Link></li>
          <li><Link to="/weather">Weather</Link></li>
          <li><Link to="/todo">Tasks</Link></li>
        </ul>
        <ul>
          <IdentityLinks />
          <li aria-hidden="true" className="nav-separator"></li>
          <li><ThemeToggle /></li>
        </ul>
      </nav>
    </header>
  );
}

import { Counter } from "./components/Counter";
import { Weather } from "./components/Weather";
import { Tasks } from "./components/Todo";
import { Home } from "./components/Home";
import { ProtectedRoute } from "./components/api-authorization/ProtectedRoute";
import { LoginPage } from "./features/identity/login/LoginPage";
import { RegisterOrganizationPage } from "./features/identity/register/RegisterOrganizationPage";
import { TenantSelector } from "./features/identity/tenants/TenantSelector";
import { InviteMemberPage } from "./features/identity/invitations/InviteMemberPage";
import { AcceptInvitationPage, RegisterFromInvitationPage } from "./features/identity/invitations/InvitationPages";
import { IdentityContextPage } from "./features/identity/context/IdentityContextPage";
import { PlatformPanel } from "./features/platform/PlatformPanel";
import {
  ConfirmPlatformInviteePage,
  PlatformMfaEnrollmentPage,
  RecoverPlatformBootstrapPage,
  RegisterPlatformInviteePage
} from "./features/platform/invitations/PlatformInvitationPages";

// The public routes are the ones a visitor reaches without a session: signing in, registering an organization,
// the two halves of an invitation, and the Platform onboarding pages — a Platform invitee has no account yet,
// and bootstrap recovery runs before any account exists at all. Everything else is behind ProtectedRoute.
//
// The MFA ceremony is protected but deliberately not the panel: an invitee holds a session with no active
// Platform tenant until the last gate completes, so requiring one would make the gates unreachable.
const AppRoutes = [
  { index: true, element: <Home /> },
  { path: '/counter', element: <Counter /> },
  { path: '/weather', element: <ProtectedRoute><Weather /></ProtectedRoute> },
  { path: '/todo', element: <ProtectedRoute><Tasks /></ProtectedRoute> },
  { path: '/login', element: <LoginPage /> },
  { path: '/organizations/register', element: <RegisterOrganizationPage /> },
  { path: '/invitations/register', element: <RegisterFromInvitationPage /> },
  { path: '/invitations/accept', element: <AcceptInvitationPage /> },
  { path: '/identity', element: <ProtectedRoute><IdentityContextPage /></ProtectedRoute> },
  { path: '/organizations/select', element: <ProtectedRoute><TenantSelector /></ProtectedRoute> },
  { path: '/members/invite', element: <ProtectedRoute><InviteMemberPage /></ProtectedRoute> },
  { path: '/platform/invitations/register', element: <RegisterPlatformInviteePage /> },
  { path: '/platform/invitations/confirm', element: <ConfirmPlatformInviteePage /> },
  { path: '/platform/bootstrap/recover', element: <RecoverPlatformBootstrapPage /> },
  { path: '/platform/mfa', element: <ProtectedRoute><PlatformMfaEnrollmentPage /></ProtectedRoute> },
  { path: '/platform', element: <ProtectedRoute><PlatformPanel /></ProtectedRoute> }
];

export default AppRoutes;

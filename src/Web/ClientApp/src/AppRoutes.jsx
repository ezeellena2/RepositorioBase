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

// The four public routes are the ones a visitor reaches without a session: signing in, registering an
// organization, and the two halves of an invitation. Everything else is behind ProtectedRoute.
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
  { path: '/members/invite', element: <ProtectedRoute><InviteMemberPage /></ProtectedRoute> }
];

export default AppRoutes;

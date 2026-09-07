import { Counter } from "./components/Counter";
import { Weather } from "./components/Weather";
import { Tasks } from "./components/Todo";
import { Home } from "./components/Home";
import { ProtectedRoute } from "./components/api-authorization/ProtectedRoute";
import { LoginPage } from "./features/identity/login/LoginPage";
import { RegisterOrganizationPage } from "./features/identity/register/RegisterOrganizationPage";
import { ChooseContextPage } from "./features/identity/register/ChooseContextPage";
import { PersonalRegisterPage, PersonalProfilePage } from "./features/identity/people/PersonalPages";
import { SessionsPage } from "./features/identity/sessions/SessionsPage";
import { ChangePasswordPage, ForgotPasswordPage, ResetPasswordPage } from "./features/identity/credentials/PasswordPages";
import { ExternalAccountsPage, ExternalReturnPage } from "./features/identity/credentials/ExternalAccountsPage";
import { ConfirmEmailPage } from "./features/identity/register/ConfirmEmailPage";
import { TenantSelector } from "./features/identity/tenants/TenantSelector";
import { InviteMemberPage } from "./features/identity/invitations/InviteMemberPage";
import { RolesPage } from "./features/identity/roles/RolesPage";
import { MembersPage } from "./features/identity/members/MembersPage";
import { AcceptInvitationPage, RegisterFromInvitationPage } from "./features/identity/invitations/InvitationPages";
import { IdentityContextPage } from "./features/identity/context/IdentityContextPage";
import { PlatformPanel } from "./features/platform/PlatformPanel";
import {
  ConfirmPlatformInviteePage,
  PlatformMfaEnrollmentPage,
  RecoverPlatformBootstrapPage,
  RegisterPlatformInviteePage
} from "./features/platform/invitations/PlatformInvitationPages";
import { MfaRecoveryPage } from "./features/platform/invitations/MfaRecoveryPage";

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
  // Where a visitor chooses what they are registering. It exists because the two answers are different products
  // for the same person, and guessing for them is how somebody ends up with the wrong one.
  { path: '/register', element: <ChooseContextPage /> },
  { path: '/organizations/register', element: <RegisterOrganizationPage /> },
  { path: '/personal/register', element: <PersonalRegisterPage /> },
  { path: '/identity/profile', element: <ProtectedRoute><PersonalProfilePage /></ProtectedRoute> },
  { path: '/identity/sessions', element: <ProtectedRoute><SessionsPage /></ProtectedRoute> },
  { path: '/identity/password', element: <ProtectedRoute><ChangePasswordPage /></ProtectedRoute> },
  { path: '/identity/external', element: <ProtectedRoute><ExternalAccountsPage /></ProtectedRoute> },
  // Public, because a provider sign-in returns here before there is a session to protect it with. It reads
  // nothing from the callback beyond which round trip it was; the handoff itself is in a server-sealed cookie.
  { path: '/external/return', element: <ExternalReturnPage /> },
  // Both are public: somebody who cannot sign in is the only person who needs them, and the reset link is opened
  // out of a mailbox by a browser holding no session.
  { path: '/credentials/forgot', element: <ForgotPasswordPage /> },
  { path: '/credentials/reset', element: <ResetPasswordPage /> },
  // Where both confirmation emails point. It is public because confirming is what an identity does before it can
  // sign in at all, so requiring a session here would make the link impossible to answer.
  { path: '/confirm-email', element: <ConfirmEmailPage /> },
  { path: '/invitations/register', element: <RegisterFromInvitationPage /> },
  { path: '/invitations/accept', element: <AcceptInvitationPage /> },
  { path: '/identity', element: <ProtectedRoute><IdentityContextPage /></ProtectedRoute> },
  { path: '/organizations/select', element: <ProtectedRoute><TenantSelector /></ProtectedRoute> },
  { path: '/members/invite', element: <ProtectedRoute><InviteMemberPage /></ProtectedRoute> },
  { path: '/roles', element: <ProtectedRoute><RolesPage /></ProtectedRoute> },
  { path: '/members', element: <ProtectedRoute><MembersPage /></ProtectedRoute> },
  { path: '/platform/invitations/register', element: <RegisterPlatformInviteePage /> },
  { path: '/platform/invitations/confirm', element: <ConfirmPlatformInviteePage /> },
  { path: '/platform/bootstrap/recover', element: <RecoverPlatformBootstrapPage /> },
  { path: '/platform/mfa', element: <ProtectedRoute><PlatformMfaEnrollmentPage /></ProtectedRoute> },
  { path: '/platform/mfa/recover', element: <ProtectedRoute><MfaRecoveryPage /></ProtectedRoute> },
  { path: '/platform', element: <ProtectedRoute><PlatformPanel /></ProtectedRoute> }
];

export default AppRoutes;

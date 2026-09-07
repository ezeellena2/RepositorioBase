using CleanArchitecture.Application.Common.Models;

namespace CleanArchitecture.Application.IdentityAccess.Common;

public static class IdentityAccessErrors
{
    public static ApplicationError UserCreationFailed() => new(
        "identity_user_creation_failed",
        ApplicationErrorCategory.Validation,
        "The identity could not be created.");

    public static ApplicationError UserDeletionFailed() => new(
        "identity_user_deletion_failed",
        ApplicationErrorCategory.Validation,
        "The identity could not be deleted.");

    public static ApplicationError TodoItemConcurrencyConflict() => new(
        "todo_item_concurrency_conflict",
        ApplicationErrorCategory.Conflict,
        "The todo item was changed by another request. Refresh it and try again.");

    public static ApplicationError InvalidRegistration() => new("invalid_registration", ApplicationErrorCategory.Validation, "The registration request is invalid.");
    public static ApplicationError InvalidConfirmation() => new("invalid_confirmation", ApplicationErrorCategory.Validation, "The confirmation request is invalid.");
    public static ApplicationError InvalidSession() => new("invalid_session", ApplicationErrorCategory.Authentication, "The supplied session is not valid.");
    public static ApplicationError RegistrationConflict() => new("registration_conflict", ApplicationErrorCategory.Conflict, "The registration cannot be completed in its current state.");

    /// <summary>
    /// Everything a caller may not do with an invitation collapses to one code. A missing token, a lapsed one, a
    /// withdrawn one, a recipient that is not the caller and an offer the inviter may not make are all the same
    /// answer, because distinguishing them would tell a token holder about state they were never shown
    /// (IA-REQ-029, SPEC section 6).
    /// </summary>
    public static ApplicationError InvalidInvitation() => new("invalid_invitation", ApplicationErrorCategory.Validation, "The invitation request is invalid.");

    /// <summary>
    /// The request was well formed and the caller was entitled to make it, but the invitation's current state
    /// refuses it: the recipient is already a member, or a competing request settled it first.
    /// </summary>
    public static ApplicationError InvitationConflict() => new("invitation_conflict", ApplicationErrorCategory.Conflict, "The invitation cannot be completed in its current state.");

    /// <summary>
    /// A session mutation kept losing its optimistic update to competing requests. The session itself is still
    /// valid, so this is a retryable conflict (IA-REQ-035) and never an unexpected failure.
    /// </summary>
    /// <summary>
    /// A Platform lifecycle change lost its conditional update to a competing one. The caller may retry against
    /// the state that won, so it is a retryable conflict rather than an unexpected failure (IA-REQ-035/043).
    /// </summary>
    public static ApplicationError PlatformTenantConcurrencyConflict() => new("platform_tenant_concurrency_conflict", ApplicationErrorCategory.Conflict, "The tenant was changed by another request. Refresh it and try again.");

    /// <summary>
    /// Everything a Platform operation may not do collapses to one code: an unknown or ineligible target, a
    /// last-owner revocation, a missing reason. Distinguishing them would describe state the caller was not
    /// shown, and the panel needs only to know the change did not happen.
    /// </summary>
    public static ApplicationError InvalidPlatformOperation() => new("invalid_platform_operation", ApplicationErrorCategory.Validation, "The Platform operation is not valid in its current state.");

    /// <summary>
    /// The caller has not proved the second factor on this session — recently enough to change something, or at
    /// all where reading requires it (IA-REQ-041/045). One code for both because the caller does the same thing
    /// about it: prove the factor. Which of the two it was is not state they were shown.
    /// </summary>
    public static ApplicationError RecentMfaRequired() => new("recent_mfa_required", ApplicationErrorCategory.Authentication, "This operation requires a recent second-factor verification.");

    /// <summary>
    /// Too many second-factor submissions for this identity. It is the one answer a bounded gate has to make
    /// distinguishable, because a client that cannot tell "wrong code" from "stop asking" will keep asking.
    /// </summary>
    public static ApplicationError MfaAttemptsExhausted(int retryAfterSeconds) =>
        new("rate_limit_exceeded", ApplicationErrorCategory.RateLimited, "Too many verification attempts. Try again later.", retryAfterSeconds: retryAfterSeconds);

    public static ApplicationError SessionConcurrencyConflict() => new("session_concurrency_conflict", ApplicationErrorCategory.Conflict, "The session was changed by another request. Try again.");

    /// <summary>
    /// Everything that stops a `Personal` context being recorded collapses to one code: this identity already owns
    /// one, or the documentary identity is already recorded. Telling those two apart would let an authenticated
    /// caller vary the number they submit and read back whether it belongs to somebody (SPEC section 14.3).
    /// </summary>
    public static ApplicationError PersonalRegistrationConflict() =>
        new("personal_registration_conflict", ApplicationErrorCategory.Conflict, "The personal context cannot be created in its current state.");

    /// <summary>This identity has no `Personal` context. No route resolves anybody else's, so there is nothing else to say.</summary>
    public static ApplicationError PersonalProfileNotFound() =>
        new("personal_profile_not_found", ApplicationErrorCategory.NotFound, "This identity has no personal profile.");

    public static ApplicationError PersonalProfileConcurrencyConflict() =>
        new("personal_profile_concurrency_conflict", ApplicationErrorCategory.Conflict, "The profile was changed by another request. Refresh it and try again.");

    /// <summary>Names the rejected member and never its value, so a refusal cannot echo what was submitted.</summary>
    public static ApplicationError ProfileFieldNotEditable(string member) =>
        new("profile_field_not_editable", ApplicationErrorCategory.Validation, $"The member '{member}' cannot be edited through this request.");

    public static ApplicationError AttemptsExhausted(int retryAfterSeconds) =>
        new("rate_limit_exceeded", ApplicationErrorCategory.RateLimited, "Too many attempts. Try again later.", retryAfterSeconds: retryAfterSeconds);

    /// <summary>
    /// The shared abuse-control store is unreachable, so the attempt was refused without being counted. It answers
    /// `503` rather than `429` because nobody spent anything (amendment A5).
    /// </summary>
    public static ApplicationError ServiceUnavailable(int retryAfterSeconds) =>
        new("service_unavailable", ApplicationErrorCategory.Unavailable, "The service is temporarily unavailable. Try again shortly.", retryAfterSeconds: retryAfterSeconds);

    /// <summary>
    /// No session of this identity carries that reference. A reference belonging to somebody else answers the same
    /// way as one that never existed, so nothing about whose it might be is disclosed (IA-REQ-030).
    /// </summary>
    public static ApplicationError SessionNotFound() =>
        new("session_not_found", ApplicationErrorCategory.NotFound, "No session of this identity carries that reference.");

    /// <summary>
    /// The action needs a proof this session does not hold: never issued, already spent, expired, or invalidated by
    /// a credential change. It is terminal rather than a conflict — retrying the same proof cannot succeed.
    /// </summary>
    /// <summary>
    /// The credential this sign-in validated was replaced before the session could be issued. It is the one
    /// sign-in refusal that is not neutral, and it can be: reaching it requires the correct old password AND a
    /// credential change only that identity could have made, so it tells a stranger nothing (C2/C4).
    /// </summary>
    public static ApplicationError CredentialSuperseded() =>
        new("credential_superseded", ApplicationErrorCategory.Authentication, "Your password changed while you were signing in. Sign in again.");

    public static ApplicationError RecentProofRequired() =>
        new("recent_proof_required", ApplicationErrorCategory.Authentication, "This operation requires a recent identity proof.");

    /// <summary>A wrong password, or an action this system does not consider sensitive. One answer for both.</summary>
    public static ApplicationError InvalidCredentialProof() =>
        new("invalid_credential_proof", ApplicationErrorCategory.Validation, "The credential proof is not valid.");

    /// <summary>
    /// The per-identity session lock was not granted within its bounded wait. It answers `429` deliberately: a
    /// status only a valid credential could reach would say something a wrong password does not.
    /// </summary>
    public static ApplicationError SessionLockUnavailable() =>
        new("rate_limit_exceeded", ApplicationErrorCategory.RateLimited, "Too many concurrent session changes. Try again.", retryAfterSeconds: 1);

    public static ApplicationError EmailConfirmationRequired() =>
        new("email_confirmation_required", ApplicationErrorCategory.Authorization, "This operation requires a confirmed email address.");

    /// <summary>
    /// Unknown, spent, superseded and expired reset links all answer this. Which one it was is state the holder of
    /// a dead link was never shown, and telling them would make the route a way to probe for live ones.
    /// </summary>
    public static ApplicationError InvalidCredentialToken() =>
        new("invalid_credential_token", ApplicationErrorCategory.Validation, "The credential token is not valid.");

    /// <summary>The configured password policy refused it, field-indexed and describing the rule rather than the value.</summary>
    public static ApplicationError PasswordPolicyFailed(IReadOnlyDictionary<string, string[]> errors) =>
        new("validation_failed", ApplicationErrorCategory.Validation, "The new password does not meet the policy.", errors);

    /// <summary>
    /// Everything a provider round trip can fail on collapses to one code: a missing, expired, spent or
    /// purpose-mismatched handoff, an unverified address, a subject that matches no link. Telling them apart would
    /// describe state the caller was never shown (IA-REQ-052).
    /// </summary>
    public static ApplicationError InvalidExternalLogin() =>
        new("invalid_external_login", ApplicationErrorCategory.Validation, "The external sign-in could not be completed.");

    /// <summary>
    /// The provider account belongs to somebody else, or its verified address does. It is a conflict rather than a
    /// refusal to say more: an automatic merge on a matching address is exactly what BR-ID-005/006 forbid.
    /// </summary>
    public static ApplicationError ExternalLoginConflict() =>
        new("external_login_conflict", ApplicationErrorCategory.Conflict, "That provider account cannot be used here.");

    public static ApplicationError ProviderAlreadyLinked() =>
        new("provider_already_linked", ApplicationErrorCategory.Conflict, "This identity already has a link for that provider.");

    public static ApplicationError ExternalLinkNotFound() =>
        new("not_found", ApplicationErrorCategory.NotFound, "This identity has no link for that provider.");

    /// <summary>
    /// Removing it would leave the person with no way in at all. It is refused rather than warned about, because
    /// the state it would produce has no route back that does not involve an operator.
    /// </summary>
    /// <summary>
    /// Every way a role change can be refused for what it is rather than for who asked: a system or retired role,
    /// a name that is not a name, a code the catalogue does not allow this tenant type, and — the one that
    /// matters — a code the actor does not itself effectively hold. They are one code because naming which
    /// permission the actor was missing would describe somebody else's authority to them (IA-REQ-053).
    /// </summary>
    /// <summary>A role this tenant does not have. Another tenant's role reaches a caller this way (IA-REQ-030).</summary>
    public static ApplicationError RoleNotFound() =>
        new("not_found", ApplicationErrorCategory.NotFound, "That role is not available.");

    public static ApplicationError InvalidRoleOperation() =>
        new("invalid_role_operation", ApplicationErrorCategory.Validation, "That role change is not valid.");

    /// <summary>A membership this tenant does not have. Another tenant's reaches a caller this way.</summary>
    public static ApplicationError MembershipNotFound() =>
        new("not_found", ApplicationErrorCategory.NotFound, "That member is not available.");

    public static ApplicationError InvalidMembershipOperation() =>
        new("invalid_membership_operation", ApplicationErrorCategory.Validation, "That membership change is not valid.");

    public static ApplicationError RoleConcurrencyConflict() =>
        new("role_concurrency_conflict", ApplicationErrorCategory.Conflict, "The role was changed by another request. Refresh it and try again.");

    public static ApplicationError MembershipConcurrencyConflict() =>
        new("membership_concurrency_conflict", ApplicationErrorCategory.Conflict, "The membership was changed by another request. Refresh it and try again.");

    /// <summary>
    /// The change would have left the organization with nobody able to administer it. Refused rather than warned
    /// about, because the state it would produce has no way back that does not involve an operator (IA-REQ-053).
    /// </summary>
    public static ApplicationError LastAdministratorRequired() =>
        new("last_administrator_required", ApplicationErrorCategory.Conflict, "An organization must keep at least one administrator.");

    /// <summary>Holding the ownership permission is necessary and never sufficient: it has to be your own.</summary>
    public static ApplicationError OwnerRequired() =>
        new("owner_required", ApplicationErrorCategory.Authorization, "Only the current owner can transfer ownership.");

    public static ApplicationError LastAuthenticatorRequired() =>
        new("last_authenticator_required", ApplicationErrorCategory.Conflict, "An identity must keep at least one way to sign in.");

    /// <summary>
    /// The one answer the public reactivation route gives to every way of failing: a forged ticket, a spent one,
    /// an expired one, one belonging to an account that has no self-service way back, and a wrong password. They
    /// are worded identically because telling them apart is exactly the account-enumeration and ticket-oracle
    /// disclosure this route exists to avoid (IA-REQ-029, IA-REQ-054).
    /// </summary>
    public static ApplicationError InvalidReactivation() =>
        new("invalid_reactivation", ApplicationErrorCategory.Validation, "The reactivation request is invalid.");

    /// <summary>
    /// The change would have left the Platform with no active owner. It is its own code rather than
    /// `last_administrator_required` because there is no higher authority to restore a Platform from, which is a
    /// different fact about a different tenant (IA-REQ-042, extended by C6).
    /// </summary>
    public static ApplicationError PlatformLastOwner() =>
        new("platform_last_owner", ApplicationErrorCategory.Conflict, "The Platform must keep at least one active owner.");

    /// <summary>The identity's state moved under a request that had already read it (IA-REQ-054).</summary>
    public static ApplicationError IdentityConcurrencyConflict() =>
        new("identity_concurrency_conflict", ApplicationErrorCategory.Conflict, "The account was changed by another request. Refresh it and try again.");

    /// <summary>
    /// Two recoveries reached the one enrollment row and this one lost. It is a conflict rather than a refusal
    /// because nothing about the caller was wrong: the row moved (IA-REQ-041).
    /// </summary>
    public static ApplicationError PlatformMfaConcurrencyConflict() =>
        new("platform_mfa_concurrency_conflict", ApplicationErrorCategory.Conflict, "The second factor was changed by another request. Try again.");

    public static ApplicationError IdentityNotFound() =>
        new("not_found", ApplicationErrorCategory.NotFound, "That identity is not available.");

    /// <summary>
    /// `Closed` and nothing else. A tombstone has no way back, which is what makes it a tombstone — and a legal
    /// hold is deliberately not one of these cases, because a hold stops erasure and never blocks a reactivation
    /// (amendment A4).
    /// </summary>
    public static ApplicationError IdentityReactivationUnavailable() =>
        new("identity_reactivation_unavailable", ApplicationErrorCategory.Authorization, "That account cannot be reactivated.");
}

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

    /// <summary>The caller holds Platform authority but has not proved the second factor recently enough.</summary>
    public static ApplicationError RecentMfaRequired() => new("recent_mfa_required", ApplicationErrorCategory.Authentication, "This operation requires a recent second-factor verification.");

    public static ApplicationError SessionConcurrencyConflict() => new("session_concurrency_conflict", ApplicationErrorCategory.Conflict, "The session was changed by another request. Try again.");
}

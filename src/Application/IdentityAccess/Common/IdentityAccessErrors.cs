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
    /// A session mutation kept losing its optimistic update to competing requests. The session itself is still
    /// valid, so this is a retryable conflict (IA-REQ-035) and never an unexpected failure.
    /// </summary>
    public static ApplicationError SessionConcurrencyConflict() => new("session_concurrency_conflict", ApplicationErrorCategory.Conflict, "The session was changed by another request. Try again.");
}

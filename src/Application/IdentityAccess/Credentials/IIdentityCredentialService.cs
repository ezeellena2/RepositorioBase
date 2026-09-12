using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.Common.Validation;
using CleanArchitecture.Application.IdentityAccess.Common;
using System.Collections.ObjectModel;

namespace CleanArchitecture.Application.IdentityAccess.Credentials;

/// <summary>What a credential change produced: either it applied, or the policy refused it and said why.</summary>
public enum CredentialWriteFailure { None, PasswordPolicy, Concurrency }

public sealed class CredentialWriteResult
{
    private static readonly ValidationErrorDetail PasswordPolicyDetail =
        new(ValidationErrorCodes.PasswordPolicy, new Dictionary<string, int>());
    private readonly ValidationErrorDetail[] _passwordPolicyDetails;

    private CredentialWriteResult(
        CredentialWriteFailure failure,
        IEnumerable<ValidationErrorDetail>? details = null)
    {
        Failure = failure;
        _passwordPolicyDetails = failure == CredentialWriteFailure.PasswordPolicy
            ? details?.ToArray() is { Length: > 0 } mapped ? mapped : [PasswordPolicyDetail]
            : [];
    }

    public bool Succeeded => Failure == CredentialWriteFailure.None;

    public CredentialWriteFailure Failure { get; }

    /// <summary>A fresh read-only projection prevents callers from mutating the result after construction.</summary>
    public IReadOnlyDictionary<string, ValidationErrorDetail[]> Errors => Failure == CredentialWriteFailure.PasswordPolicy
        ? new ReadOnlyDictionary<string, ValidationErrorDetail[]>(new Dictionary<string, ValidationErrorDetail[]>(StringComparer.Ordinal)
        {
            ["newPassword"] = _passwordPolicyDetails.ToArray()
        })
        : new ReadOnlyDictionary<string, ValidationErrorDetail[]>(new Dictionary<string, ValidationErrorDetail[]>(StringComparer.Ordinal));

    public static CredentialWriteResult Applied() => new(CredentialWriteFailure.None);

    public static CredentialWriteResult PasswordPolicy(IEnumerable<ValidationErrorDetail>? details = null) =>
        new(CredentialWriteFailure.PasswordPolicy, details);

    public static CredentialWriteResult Concurrency() => new(CredentialWriteFailure.Concurrency);

    public ApplicationError ToApplicationError() => Failure switch
    {
        CredentialWriteFailure.PasswordPolicy => IdentityAccessErrors.PasswordPolicyFailed(Errors),
        CredentialWriteFailure.Concurrency => IdentityAccessErrors.IdentityConcurrencyConflict(),
        _ => throw new InvalidOperationException("A successful credential write has no application error.")
    };
}

/// <summary>
/// The credential half of the identity boundary. Registration already had lookup, creation and confirmation; this
/// adds replacing a password, which ASP.NET Identity owns and the application layer must not reach into directly.
/// </summary>
public interface IIdentityCredentialService
{
    /// <summary>
    /// Replaces the password without asking for the previous one. What authorizes it is decided above: a spent
    /// reset token, or a recent identity proof. There is no `currentPassword` field anywhere in the system.
    /// </summary>
    Task<CredentialWriteResult> ReplacePasswordAsync(Guid identityId, string newPassword, CancellationToken cancellationToken);

    /// <summary>Whether this identity has a password at all. A provider-only account has none to change.</summary>
    Task<bool> HasPasswordAsync(Guid identityId, CancellationToken cancellationToken);
}

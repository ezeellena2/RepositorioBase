namespace CleanArchitecture.Application.IdentityAccess.Credentials;

/// <summary>What a credential change produced: either it applied, or the policy refused it and said why.</summary>
public sealed record CredentialWriteResult(bool Succeeded, IReadOnlyDictionary<string, string[]> Errors)
{
    public static CredentialWriteResult Applied() => new(true, new Dictionary<string, string[]>());

    public static CredentialWriteResult Refused(IReadOnlyDictionary<string, string[]> errors) => new(false, errors);
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

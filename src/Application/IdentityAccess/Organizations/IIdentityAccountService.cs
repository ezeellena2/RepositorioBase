using CleanArchitecture.Domain.IdentityAccess.Identities;

namespace CleanArchitecture.Application.IdentityAccess.Organizations;

/// <summary>
/// An identity as the application layer is allowed to see it. <see cref="IsActive"/> is derived rather than
/// stored beside the state, so there is exactly one answer to "may this account act" and it is the state
/// (IA-REQ-054).
/// </summary>
public sealed record IdentityAccount(Guid Id, string Email, IdentityAccountStatus Status, string? PreferredLanguage = null)
{
    public bool IsActive => Status == IdentityAccountStatus.Active;
}
public sealed record IdentityAccountValidationResult(bool IsValid, IReadOnlyList<string> Errors)
{
    public IdentityAccountValidationResult(bool isValid)
        : this(isValid, Array.Empty<string>())
    {
    }
}
public sealed record IdentityAccountCreationResult(IdentityAccount? Account, bool IsValidationFailure);

/// <summary>Identity boundary used by registration without exposing ASP.NET Identity to the application layer.</summary>
public interface IIdentityAccountService
{
    Task<IdentityAccount?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken);

    Task<IdentityAccount?> FindByIdAsync(Guid identityId, CancellationToken cancellationToken);

    Task<IdentityAccount?> ValidateCredentialsAsync(string normalizedEmail, string password, CancellationToken cancellationToken);

    Task<IdentityAccountValidationResult> ValidatePendingRegistrationAsync(string normalizedEmail, string password, CancellationToken cancellationToken);

    /// <summary>
    /// Validates a password against the configured policy and nothing else. It reads no state and therefore
    /// answers identically for an address that exists and one that does not, which is what lets a caller refuse a
    /// weak password before deciding anything — and so without the refusal itself disclosing whether the address
    /// is taken, or whether a token was real (IA-REQ-029).
    /// </summary>
    Task<IdentityAccountValidationResult> ValidatePasswordAsync(string password, CancellationToken cancellationToken);

    Task<IdentityAccountCreationResult> CreatePendingAsync(
        string normalizedEmail,
        string password,
        string preferredLanguage,
        CancellationToken cancellationToken);

    /// <summary>
    /// The irreversible hash the configured hasher produces for this password, computed without reading any state.
    /// <para>
    /// Registration takes the password once, at initiation, and must not keep it: the account it belongs to does
    /// not exist yet and may never exist. Keeping the hash lets the later, proved request create the identity with
    /// the credential the person actually chose, while nothing recoverable is ever stored.
    /// </para>
    /// </summary>
    string HashPassword(string password);

    /// <summary>
    /// Creates the unconfirmed identity from a hash this service produced earlier. The password policy was applied
    /// when that hash was computed, so it is not re-applied here; what is still enforced is the uniqueness of the
    /// normalized address, which is why this can fail.
    /// </summary>
    Task<IdentityAccountCreationResult> CreatePendingFromHashAsync(
        string normalizedEmail,
        string passwordHash,
        string preferredLanguage,
        CancellationToken cancellationToken);

    Task ActivateAsync(Guid identityId, CancellationToken cancellationToken);

    /// <summary>
    /// Changes only the caller-owned language preference. It deliberately bypasses UserManager so a presentation
    /// preference cannot rotate a security stamp, cookie or session (IA-REQ-059).
    /// </summary>
    Task<bool> SetPreferredLanguageAsync(Guid identityId, string language, CancellationToken cancellationToken);

    /// <summary>
    /// Whether this password still opens this identity, asked about an identity that is not allowed to sign in.
    /// <para>
    /// Sign-in cannot answer this: it refuses a non-`Active` account before it looks at any password, which is
    /// the whole point of the state. Coming back from parking still has to check a real credential, so the check
    /// exists separately — and it is deliberately the same check, lockout window and failure counting included,
    /// so this route is not a quieter place to guess a password than the front door is (IA-REQ-019, IA-REQ-054).
    /// </para>
    /// </summary>
    Task<bool> VerifyPasswordAsync(Guid identityId, string password, CancellationToken cancellationToken);

    /// <summary>
    /// Moves this identity from one named state to another, and answers whether this call is the one that moved
    /// it.
    /// <para>
    /// One conditional statement guarded on `(identityId, status = expected)`, so two requests attempting the
    /// same transition cannot both succeed: the second finds its own precondition already gone and answers
    /// <see langword="false"/>, which the caller turns into `identity_concurrency_conflict`. The winning write
    /// rotates the row's `ConcurrencyStamp`, so any copy another request is still holding is stale by the time it
    /// tries to save (IA-REQ-054).
    /// </para>
    /// </summary>
    Task<bool> TryTransitionAsync(
        Guid identityId,
        IdentityAccountStatus expected,
        IdentityAccountStatus next,
        CancellationToken cancellationToken);
}

namespace CleanArchitecture.Application.IdentityAccess.Organizations;

public sealed record IdentityAccount(Guid Id, string Email, bool IsActive);
public sealed record IdentityAccountValidationResult(bool IsValid);
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

    Task<IdentityAccountCreationResult> CreatePendingAsync(string normalizedEmail, string password, CancellationToken cancellationToken);

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
    Task<IdentityAccountCreationResult> CreatePendingFromHashAsync(string normalizedEmail, string passwordHash, CancellationToken cancellationToken);

    Task ActivateAsync(Guid identityId, CancellationToken cancellationToken);
}

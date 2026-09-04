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

    Task ActivateAsync(Guid identityId, CancellationToken cancellationToken);
}

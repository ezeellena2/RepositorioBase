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

    Task<IdentityAccountCreationResult> CreatePendingAsync(string normalizedEmail, string password, CancellationToken cancellationToken);

    Task ActivateAsync(Guid identityId, CancellationToken cancellationToken);
}

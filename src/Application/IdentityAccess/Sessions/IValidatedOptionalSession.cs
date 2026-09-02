namespace CleanArchitecture.Application.IdentityAccess.Sessions;

/// <summary>Optional trusted session input for public flows; only a validated persisted session is ever exposed.</summary>
public interface IValidatedOptionalSession
{
    Guid? IdentityId { get; }
    string? Email { get; }
    bool IsInvalid { get; }
}

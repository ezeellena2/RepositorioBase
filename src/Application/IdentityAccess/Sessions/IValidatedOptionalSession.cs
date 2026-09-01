namespace CleanArchitecture.Application.IdentityAccess.Sessions;

/// <summary>Trusted optional session input. Production supplies no session until Task 8.</summary>
public interface IValidatedOptionalSession
{
    Guid? IdentityId { get; }
    string? Email { get; }
    bool IsInvalid { get; }
}

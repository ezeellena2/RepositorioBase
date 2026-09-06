namespace CleanArchitecture.Application.IdentityAccess.People;

/// <summary>The profile's operational stamps: its opaque row version, and when it last changed.</summary>
public readonly record struct PersonalProfileStamp(string Version, DateTimeOffset UpdatedAt);

/// <summary>
/// Reads the two shadow properties a profile carries. They are a port rather than aggregate members because no
/// domain rule depends on them — the row version belongs to PostgreSQL and the timestamp to the interceptor — and
/// the application context deliberately exposes no entry-level API that could reach them.
/// </summary>
public interface IPersonalProfileStamps
{
    Task<PersonalProfileStamp?> ReadAsync(Guid identityId, CancellationToken cancellationToken);
}

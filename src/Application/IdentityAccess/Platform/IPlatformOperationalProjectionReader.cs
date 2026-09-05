using CleanArchitecture.Application.IdentityAccess.Platform.Queries;

namespace CleanArchitecture.Application.IdentityAccess.Platform;

/// <summary>
/// Reads the four Platform directories (IA-REQ-044).
/// <para>
/// It is a port, and returns projections rather than entities, precisely so that the allowlist is a property of a
/// type rather than of a query someone remembered to write carefully. A handler holding a DbSet could always
/// select one more column; a handler holding this cannot.
/// </para>
/// </summary>
public interface IPlatformOperationalProjectionReader
{
    Task<PlatformDirectoryPage<PlatformOrganizationProjection>> ReadOrganizationsAsync(PlatformDirectoryQuery query, CancellationToken cancellationToken);

    Task<PlatformDirectoryPage<PlatformIdentityProjection>> ReadIdentitiesAsync(PlatformDirectoryQuery query, CancellationToken cancellationToken);

    Task<PlatformDirectoryPage<PlatformAdministratorProjection>> ReadAdministratorsAsync(PlatformDirectoryQuery query, CancellationToken cancellationToken);

    Task<PlatformDirectoryPage<PlatformAuditEventProjection>> ReadAuditAsync(PlatformDirectoryQuery query, CancellationToken cancellationToken);
}

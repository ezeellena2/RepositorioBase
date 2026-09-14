using CleanArchitecture.Application.Common.Models;
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
    Task<PaginatedList<PlatformOrganizationProjection>> ReadOrganizationsAsync(PaginationQuery query, CancellationToken cancellationToken);

    Task<PaginatedList<PlatformIdentityProjection>> ReadIdentitiesAsync(PaginationQuery query, CancellationToken cancellationToken);

    Task<PaginatedList<PlatformAdministratorProjection>> ReadAdministratorsAsync(PaginationQuery query, CancellationToken cancellationToken);

    Task<PaginatedList<PlatformAuditEventProjection>> ReadAuditAsync(PaginationQuery query, CancellationToken cancellationToken);
}

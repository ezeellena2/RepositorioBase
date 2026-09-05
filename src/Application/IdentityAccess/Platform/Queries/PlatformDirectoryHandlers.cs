using CleanArchitecture.Application.Common.Models;

namespace CleanArchitecture.Application.IdentityAccess.Platform.Queries;

/// <summary>
/// The four directory handlers. They are this thin on purpose: authorization is the pipeline's, the allowlist is
/// the projection's, and the paging is the reader's, so there is nothing left here to get wrong.
/// </summary>
public sealed class ListPlatformOrganizationsQueryHandler(IPlatformOperationalProjectionReader reader)
    : IRequestHandler<ListPlatformOrganizationsQuery, Result<PlatformDirectoryPage<PlatformOrganizationProjection>>>
{
    public async Task<Result<PlatformDirectoryPage<PlatformOrganizationProjection>>> Handle(ListPlatformOrganizationsQuery request, CancellationToken cancellationToken) =>
        Result<PlatformDirectoryPage<PlatformOrganizationProjection>>.Success(await reader.ReadOrganizationsAsync(request.Query, cancellationToken));
}

public sealed class ListPlatformIdentitiesQueryHandler(IPlatformOperationalProjectionReader reader)
    : IRequestHandler<ListPlatformIdentitiesQuery, Result<PlatformDirectoryPage<PlatformIdentityProjection>>>
{
    public async Task<Result<PlatformDirectoryPage<PlatformIdentityProjection>>> Handle(ListPlatformIdentitiesQuery request, CancellationToken cancellationToken) =>
        Result<PlatformDirectoryPage<PlatformIdentityProjection>>.Success(await reader.ReadIdentitiesAsync(request.Query, cancellationToken));
}

public sealed class ListPlatformAdministratorsQueryHandler(IPlatformOperationalProjectionReader reader)
    : IRequestHandler<ListPlatformAdministratorsQuery, Result<PlatformDirectoryPage<PlatformAdministratorProjection>>>
{
    public async Task<Result<PlatformDirectoryPage<PlatformAdministratorProjection>>> Handle(ListPlatformAdministratorsQuery request, CancellationToken cancellationToken) =>
        Result<PlatformDirectoryPage<PlatformAdministratorProjection>>.Success(await reader.ReadAdministratorsAsync(request.Query, cancellationToken));
}

public sealed class ListPlatformAuditQueryHandler(IPlatformOperationalProjectionReader reader)
    : IRequestHandler<ListPlatformAuditQuery, Result<PlatformDirectoryPage<PlatformAuditEventProjection>>>
{
    public async Task<Result<PlatformDirectoryPage<PlatformAuditEventProjection>>> Handle(ListPlatformAuditQuery request, CancellationToken cancellationToken) =>
        Result<PlatformDirectoryPage<PlatformAuditEventProjection>>.Success(await reader.ReadAuditAsync(request.Query, cancellationToken));
}

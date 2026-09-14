using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.IdentityAccess.Common;

namespace CleanArchitecture.Application.IdentityAccess.Platform.Queries;

/// <summary>
/// The one condition the pipeline cannot express, asked once for all four directories (IA-REQ-045).
/// <para>
/// Tenant and permission are the pipeline's, and both are satisfied by a session that presented only a password:
/// signing in selects a sole active tenant, and the membership carries the read permissions from the moment it
/// was activated. What the pipeline cannot ask is whether <em>this</em> session proved the second factor, because
/// that evidence lives on the enrollment rather than on the request. So it is asked here, in one place the four
/// handlers share, and a fifth directory cannot be written without meeting it.
/// </para>
/// </summary>
internal static class PlatformDirectoryGate
{
    internal static async Task<Result<T>?> RefusalAsync<T>(IPlatformMfaSessionProof proof, CancellationToken cancellationToken) =>
        await proof.HasProvedFactorAsync(cancellationToken)
            ? null
            : Result<T>.Failure(IdentityAccessErrors.RecentMfaRequired());
}

/// <summary>
/// The four directory handlers. They are this thin on purpose: authorization is the pipeline's, the allowlist is
/// the projection's, and the paging is the reader's, so there is nothing left here to get wrong — except the one
/// question the pipeline cannot ask, which they all ask the same way.
/// </summary>
public sealed class ListPlatformOrganizationsQueryHandler(IPlatformOperationalProjectionReader reader, IPlatformMfaSessionProof proof)
    : IRequestHandler<ListPlatformOrganizationsQuery, Result<PaginatedList<PlatformOrganizationProjection>>>
{
    public async Task<Result<PaginatedList<PlatformOrganizationProjection>>> Handle(ListPlatformOrganizationsQuery request, CancellationToken cancellationToken) =>
        await PlatformDirectoryGate.RefusalAsync<PaginatedList<PlatformOrganizationProjection>>(proof, cancellationToken)
        ?? Result<PaginatedList<PlatformOrganizationProjection>>.Success(await reader.ReadOrganizationsAsync(request.Query, cancellationToken));
}

public sealed class ListPlatformIdentitiesQueryHandler(IPlatformOperationalProjectionReader reader, IPlatformMfaSessionProof proof)
    : IRequestHandler<ListPlatformIdentitiesQuery, Result<PaginatedList<PlatformIdentityProjection>>>
{
    public async Task<Result<PaginatedList<PlatformIdentityProjection>>> Handle(ListPlatformIdentitiesQuery request, CancellationToken cancellationToken) =>
        await PlatformDirectoryGate.RefusalAsync<PaginatedList<PlatformIdentityProjection>>(proof, cancellationToken)
        ?? Result<PaginatedList<PlatformIdentityProjection>>.Success(await reader.ReadIdentitiesAsync(request.Query, cancellationToken));
}

public sealed class ListPlatformAdministratorsQueryHandler(IPlatformOperationalProjectionReader reader, IPlatformMfaSessionProof proof)
    : IRequestHandler<ListPlatformAdministratorsQuery, Result<PaginatedList<PlatformAdministratorProjection>>>
{
    public async Task<Result<PaginatedList<PlatformAdministratorProjection>>> Handle(ListPlatformAdministratorsQuery request, CancellationToken cancellationToken) =>
        await PlatformDirectoryGate.RefusalAsync<PaginatedList<PlatformAdministratorProjection>>(proof, cancellationToken)
        ?? Result<PaginatedList<PlatformAdministratorProjection>>.Success(await reader.ReadAdministratorsAsync(request.Query, cancellationToken));
}

public sealed class ListPlatformAuditQueryHandler(IPlatformOperationalProjectionReader reader, IPlatformMfaSessionProof proof)
    : IRequestHandler<ListPlatformAuditQuery, Result<PaginatedList<PlatformAuditEventProjection>>>
{
    public async Task<Result<PaginatedList<PlatformAuditEventProjection>>> Handle(ListPlatformAuditQuery request, CancellationToken cancellationToken) =>
        await PlatformDirectoryGate.RefusalAsync<PaginatedList<PlatformAuditEventProjection>>(proof, cancellationToken)
        ?? Result<PaginatedList<PlatformAuditEventProjection>>.Success(await reader.ReadAuditAsync(request.Query, cancellationToken));
}

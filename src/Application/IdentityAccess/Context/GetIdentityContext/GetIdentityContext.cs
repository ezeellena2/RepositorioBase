using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.Common.Security;
using CleanArchitecture.Application.IdentityAccess.Authorization;

namespace CleanArchitecture.Application.IdentityAccess.Context.GetIdentityContext;

[Authorize(Permissions.IdentityContextRead, false)]
public sealed record GetIdentityContextQuery : IRequest<Result<IdentityContext>>;

public sealed record IdentityContext(
    Guid IdentityId,
    string DisplayName,
    bool EmailConfirmed,
    TenantContext? ActiveTenant,
    IReadOnlyList<TenantContext> AvailableTenants,
    IReadOnlyList<string> Permissions,
    DateTimeOffset SessionExpiresAt,
    bool RequiresTwoFactor);

public sealed record TenantContext(Guid Id, string Type, string Name);

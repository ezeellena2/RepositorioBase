using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.Common.Security;
using CleanArchitecture.Application.IdentityAccess.Authorization;
using CleanArchitecture.Application.IdentityAccess.Context.GetIdentityContext;
using CleanArchitecture.Domain.IdentityAccess.Tenants;

namespace CleanArchitecture.Application.IdentityAccess.Context.SelectTenant;

[Authorize(Permissions.IdentityContextSelect, false)]
public sealed record SelectTenantCommand(TenantId TenantId) : IRequest<Result<IdentityContext>>;

using CleanArchitecture.Application.IdentityAccess.Context.GetIdentityContext;

namespace CleanArchitecture.Web.IdentityEndpoints.Contracts;

public sealed record IdentityContextResponse(
    IdentityContextUserResponse User,
    IdentityContextTenantResponse? ActiveTenant,
    IReadOnlyList<IdentityContextTenantResponse> AvailableTenants,
    IReadOnlyList<string> Permissions,
    IdentityContextSessionResponse Session)
{
    public static IdentityContextResponse From(IdentityContext context) => new(
        new IdentityContextUserResponse(context.IdentityId.ToString("N"), context.DisplayName, context.EmailConfirmed),
        context.ActiveTenant is null ? null : IdentityContextTenantResponse.From(context.ActiveTenant),
        context.AvailableTenants.Select(IdentityContextTenantResponse.From).ToArray(),
        context.Permissions,
        new IdentityContextSessionResponse(context.SessionExpiresAt, context.RequiresTwoFactor));
}

public sealed record IdentityContextUserResponse(string Id, string DisplayName, bool EmailConfirmed);
public sealed record IdentityContextTenantResponse(Guid Id, string Type, string Name)
{
    public static IdentityContextTenantResponse From(TenantContext tenant) => new(tenant.Id, tenant.Type, tenant.Name);
}
public sealed record IdentityContextSessionResponse(DateTimeOffset ExpiresAt, bool RequiresTwoFactor);

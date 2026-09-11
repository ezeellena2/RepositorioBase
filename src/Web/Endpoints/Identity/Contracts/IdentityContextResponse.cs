using CleanArchitecture.Application.IdentityAccess.Context.GetIdentityContext;

namespace CleanArchitecture.Web.IdentityEndpoints.Contracts;

public sealed record IdentityContextResponse(
    IdentityContextUserResponse User,
    string? PreferredLanguage,
    IdentityContextTenantResponse? ActiveTenant,
    IReadOnlyList<IdentityContextTenantResponse> AvailableTenants,
    IReadOnlyList<string> Permissions,
    IdentityContextSessionResponse Session,
    IdentityContextPersonalDataResponse PersonalData)
{
    public static IdentityContextResponse From(IdentityContext context) => new(
        new IdentityContextUserResponse(context.IdentityId.ToString("N"), context.DisplayName, context.EmailConfirmed),
        context.PreferredLanguage,
        context.ActiveTenant is null ? null : IdentityContextTenantResponse.From(context.ActiveTenant),
        context.AvailableTenants.Select(IdentityContextTenantResponse.From).ToArray(),
        context.Permissions,
        new IdentityContextSessionResponse(context.SessionExpiresAt, context.RequiresTwoFactor),
        new IdentityContextPersonalDataResponse(context.PersonalDataMode));
}

public sealed record IdentityContextUserResponse(string Id, string DisplayName, bool EmailConfirmed);
public sealed record IdentityContextTenantResponse(Guid Id, string Type, string Name)
{
    public static IdentityContextTenantResponse From(TenantContext tenant) => new(tenant.Id, tenant.Type, tenant.Name);
}
public sealed record IdentityContextSessionResponse(DateTimeOffset ExpiresAt, bool RequiresTwoFactor);

/// <summary>
/// What the deployment is doing with personal data, so a client can say so plainly rather than a person having to
/// assume. It is derived by the server from configuration and is never a request field (IA-REQ-056).
/// </summary>
public sealed record IdentityContextPersonalDataResponse(string Mode);

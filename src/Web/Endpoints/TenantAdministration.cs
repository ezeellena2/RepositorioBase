using CleanArchitecture.Web.Infrastructure;

namespace CleanArchitecture.Web.Endpoints;

/// <summary>Tenant-addressed routes. The route tenant is compared, never trusted, by the handler.</summary>
public sealed class TenantAdministration : IEndpointGroup
{
    public static string? RoutePrefix => "/api/tenants";

    public static void Map(RouteGroupBuilder group)
    {
        global::CleanArchitecture.Web.IdentityEndpoints.InvitationEndpoints.MapTenantScoped(group);
        global::CleanArchitecture.Web.IdentityEndpoints.RoleEndpoints.MapTenantScoped(group);
    }
}

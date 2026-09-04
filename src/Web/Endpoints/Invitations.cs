using CleanArchitecture.Web.Infrastructure;

namespace CleanArchitecture.Web.Endpoints;

/// <summary>
/// Invitation routes a recipient reaches before they belong to any tenant. They live outside /api/identity so a
/// caller cannot confuse them with the identity self-service surface.
/// </summary>
public sealed class Invitations : IEndpointGroup
{
    public static string? RoutePrefix => "/api/invitations";

    public static void Map(RouteGroupBuilder group) =>
        global::CleanArchitecture.Web.IdentityEndpoints.InvitationEndpoints.MapPublic(group);
}

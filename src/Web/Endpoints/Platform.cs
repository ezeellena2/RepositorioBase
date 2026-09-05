using CleanArchitecture.Web.Infrastructure;

namespace CleanArchitecture.Web.Endpoints;

/// <summary>
/// The Platform surface (SPEC section 6). It is one group so every Platform route shares a prefix a reader can
/// scan for, and so nothing Platform-shaped can appear under the identity self-service surface by accident.
/// </summary>
public sealed class Platform : IEndpointGroup
{
    public static string? RoutePrefix => "/api/platform";

    public static void Map(RouteGroupBuilder group)
    {
        global::CleanArchitecture.Web.PlatformEndpoints.PlatformInvitationEndpoints.Map(group);
        global::CleanArchitecture.Web.PlatformEndpoints.PlatformMfaEndpoints.Map(group);
    }
}

using CleanArchitecture.Application.IdentityAccess.ExternalLogins;
using CleanArchitecture.Domain.IdentityAccess.ExternalLogins;

namespace CleanArchitecture.Infrastructure.IdentityAccess;

/// <summary>
/// What a provider round trip looks like from a host that has no browser in front of it.
/// <para>
/// The background worker resolves the same handlers as the web application, and a handler that could not be
/// constructed there would be a container the worker refuses to build. It answers the only truthful thing: a
/// process with no request pipeline offers no provider and is carrying no handoff. The web application registers
/// its own, which reads the sealed cookie, after this one.
/// </para>
/// </summary>
public sealed class NoExternalHandoffContext : IExternalHandoffContext
{
    public Guid? Current => null;

    public ExternalAuthorizationPurpose? CurrentPurpose => null;

    public bool IsConfigured(string provider) => false;
}

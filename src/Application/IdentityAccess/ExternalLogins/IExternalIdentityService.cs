namespace CleanArchitecture.Application.IdentityAccess.ExternalLogins;

/// <summary>
/// One provider account an identity has explicitly linked. It names the provider, the address that provider
/// asserted, when the person linked it, and an opaque handle to key it by. The subject identifier the provider
/// uses, and every token, stay on the server.
/// </summary>
public sealed record ExternalLinkView(string Handle, string Provider, string ProviderEmail, DateTimeOffset LinkedAt);

/// <summary>
/// What this identity has linked, and what this deployment offers to link. The second half is here because only
/// the server knows it: a provider with no client configured has no middleware and no route that can succeed.
/// </summary>
public sealed record ExternalLinkList(IReadOnlyList<ExternalLinkView> Items, IReadOnlyList<string> Available);

/// <summary>
/// The provider-link half of the identity boundary, over ASP.NET Identity's own `AspNetUserLogins` rather than a
/// second account registry: its provider-key uniqueness is already the rule that stops one subject being owned by
/// two identities, and duplicating it would mean two rules that can disagree.
/// </summary>
public interface IExternalIdentityService
{
    Task<Guid?> FindIdentityBySubjectAsync(string provider, string subject, CancellationToken cancellationToken);

    Task<IReadOnlyList<ExternalLinkView>> ListAsync(Guid identityId, CancellationToken cancellationToken);

    Task<bool> HasLinkAsync(Guid identityId, string provider, CancellationToken cancellationToken);

    Task<bool> LinkAsync(Guid identityId, string provider, string subject, string providerEmail, CancellationToken cancellationToken);

    Task<bool> UnlinkAsync(Guid identityId, string provider, CancellationToken cancellationToken);

    /// <summary>
    /// How many ways this identity can still get in: its password, if it has one, plus each provider link. It is
    /// what stops an unlink leaving somebody locked out of their own account.
    /// </summary>
    Task<int> AuthenticatorCountAsync(Guid identityId, CancellationToken cancellationToken);

    /// <summary>
    /// Creates an identity from a provider assertion: confirmed because the provider verified the address, with no
    /// password, no tenant and no membership. Onboarding into a context is Task 20's explicit choice, not a
    /// side effect of signing in (IA-REQ-020 as C4 amends it).
    /// </summary>
    Task<Guid?> CreateFromProviderAsync(string normalizedEmail, CancellationToken cancellationToken);
}

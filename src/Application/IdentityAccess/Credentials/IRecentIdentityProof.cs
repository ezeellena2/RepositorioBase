using CleanArchitecture.Domain.IdentityAccess.Sessions;

namespace CleanArchitecture.Application.IdentityAccess.Credentials;

/// <summary>
/// The closed set of things a recent identity proof may authorize. It is closed on purpose: proving in order to
/// unlink a provider must not also authorize changing a password, and an open set would make that impossible to
/// state (IA-REQ-051).
/// </summary>
public static class ProofActions
{
    public const string PasswordChange = "credentials.password.change";
    public const string ExternalLink = "external.link";
    public const string ExternalUnlink = "external.unlink";
    public const string RevokeOtherSessions = "sessions.revoke-others";
    public const string RevokeOneSession = "sessions.revoke-one";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        PasswordChange,
        ExternalLink,
        ExternalUnlink,
        RevokeOtherSessions,
        RevokeOneSession
    };
}

/// <summary>
/// The store behind IA-REQ-051. Nothing here hands a proof to a caller: issuing writes a server-side row, and
/// spending one looks it up from the request's own identity, session and action.
/// </summary>
public interface IRecentIdentityProofStore
{
    /// <summary>Records a fresh proof, replacing any live one for the same identity, session and action.</summary>
    Task IssueAsync(Guid identityId, UserSessionId sessionId, string action, RecentIdentityProofMethod method, CancellationToken cancellationToken);

    /// <summary>
    /// Spends the live proof for this identity, session and action, if there is one whose security version still
    /// matches. Answers whether it was spent; a second call for the same proof answers <see langword="false"/>.
    /// </summary>
    Task<bool> TryConsumeAsync(Guid identityId, UserSessionId sessionId, string action, CancellationToken cancellationToken);

    /// <summary>The identity's current security version. An identity with no row is at version zero.</summary>
    Task<long> CurrentVersionAsync(Guid identityId, CancellationToken cancellationToken);

    /// <summary>
    /// Advances the security version, which invalidates every outstanding proof for that identity at once. Called
    /// by anything that changes a credential or an authenticator.
    /// </summary>
    Task AdvanceVersionAsync(Guid identityId, CancellationToken cancellationToken);
}

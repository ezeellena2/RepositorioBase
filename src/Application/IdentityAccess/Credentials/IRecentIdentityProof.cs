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

    /// <summary>
    /// Changing what a role confers, and changing which roles a member holds (amendment D2). They are two actions
    /// rather than one for the same reason the rest of this set is split: proving in order to edit a role is not
    /// permission to hand that role to somebody.
    /// </summary>
    public const string RoleChange = "roles.change";

    public const string MemberRoleChange = "members.roles.change";

    /// <summary>Giving the organization away, which is the one change nobody can undo alone.</summary>
    public const string OwnershipTransfer = "tenant.ownership.transfer";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        PasswordChange,
        ExternalLink,
        ExternalUnlink,
        RevokeOtherSessions,
        RevokeOneSession,
        RoleChange,
        MemberRoleChange,
        OwnershipTransfer
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

    /// <summary>
    /// The same advance, plus the date the password itself changed. Only a password change may call it: linking a
    /// provider advances the version too, and reporting that as a password change would be a lie told to the one
    /// person who would notice.
    /// </summary>
    Task RecordPasswordChangeAsync(Guid identityId, CancellationToken cancellationToken);

    /// <summary>When this identity's password last changed, or <see langword="null"/> if it never has.</summary>
    Task<DateTimeOffset?> PasswordUpdatedAtAsync(Guid identityId, CancellationToken cancellationToken);
}

using CleanArchitecture.Domain.IdentityAccess.Platform;

namespace CleanArchitecture.Application.IdentityAccess.Platform.Bootstrap;

/// <summary>
/// Decides whether the pending owner invitation may be reissued, and to whom (IA-REQ-040).
/// <para>
/// It is a plain decision object rather than a FluentValidation rule on purpose. A pipeline validator answers a
/// malformed request with a typed <c>400</c>, and every state this looks at must instead answer with the same
/// neutral <c>202</c> a successful recovery does — otherwise the response itself tells an anonymous caller whether
/// a bootstrap is pending, whether it has expired, and which address it is for.
/// </para>
/// <para>
/// The recipient it derives is the invitation's own, never the configured value. The two must agree — a
/// configuration edited after bootstrap must not move the pending owner — but where the address comes from is the
/// invitation, so that recovery cannot become a way to redirect it.
/// </para>
/// </summary>
public sealed class RecoverPendingPlatformOwnerInvitationValidator
{
    public PlatformRecoveryDecision Decide(PlatformAdminInvitation? invitation, string? configuredEmail, DateTimeOffset now)
    {
        // No configuration means no bootstrap was ever asked for, so there is nothing to recover.
        if (string.IsNullOrWhiteSpace(configuredEmail)) return PlatformRecoveryDecision.Ineligible;

        string configured;
        try
        {
            configured = PlatformAdminInvitation.Canonicalize(configuredEmail);
        }
        catch (ArgumentException)
        {
            return PlatformRecoveryDecision.Ineligible;
        }

        if (invitation is null || !invitation.IsOwner) return PlatformRecoveryDecision.Ineligible;

        // A completed activation permanently closes bootstrap, and a withdrawn offer is not one to revive.
        if (invitation.Status != PlatformAdminInvitationStatus.Pending) return PlatformRecoveryDecision.Ineligible;

        // Only an offer that cannot arrive. One still in flight must not be rotated: that would invalidate a token
        // the recipient may be about to use, which is the opposite of recovering it.
        if (!invitation.IsRecoverableAt(now)) return PlatformRecoveryDecision.Ineligible;

        // A configuration change must not replace or elevate the pending owner.
        if (!string.Equals(invitation.NormalizedEmail, configured, StringComparison.Ordinal))
        {
            return PlatformRecoveryDecision.Ineligible;
        }

        return new PlatformRecoveryDecision(true, invitation.NormalizedEmail);
    }
}

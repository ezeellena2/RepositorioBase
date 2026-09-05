using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.Common.Security;

namespace CleanArchitecture.Application.IdentityAccess.Platform.Bootstrap;

/// <summary>
/// Reissues the pending bootstrap owner invitation when its token can no longer arrive (IA-REQ-040).
/// <para>
/// It carries nothing at all, and that is the requirement rather than an omission. Accepting an email, an identity
/// or a replacement recipient would make this the one route that can point the system's highest authority at an
/// address of the caller's choosing. The destination is derived only from the configured and pending recipient,
/// which is also what lets it work before any ApplicationUser exists.
/// </para>
/// </summary>
public sealed record RecoverPendingPlatformOwnerInvitationCommand : IRequest<Result>, IPublicRequest;

/// <summary>Whether recovery may run, and for whom. There is no third option: the recipient is never chosen.</summary>
public sealed record PlatformRecoveryDecision(bool IsEligible, string? Recipient)
{
    public static PlatformRecoveryDecision Ineligible { get; } = new(false, null);
}

/// <summary>The lease a limiter grants, and how long to wait when it does not.</summary>
public sealed record PlatformRecoveryLease(bool IsAcquired, int RetryAfterSeconds)
{
    public static PlatformRecoveryLease Granted { get; } = new(true, 0);
}

/// <summary>
/// Bounds how often recovery may run.
/// <para>
/// It is keyed by the pending invitation and by the transport the request arrived on, both of which the
/// implementation derives — never by anything the caller supplies. A limiter an attacker can re-key per attempt
/// is not a limit, and a bodyless request gives them nothing to re-key with only if the key comes from elsewhere.
/// </para>
/// </summary>
public interface IPlatformBootstrapRecoveryRateLimiter
{
    Task<PlatformRecoveryLease> TryAcquireAsync(Guid? pendingInvitationId, CancellationToken cancellationToken);
}

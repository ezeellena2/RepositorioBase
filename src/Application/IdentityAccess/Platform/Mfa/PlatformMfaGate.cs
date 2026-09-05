using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.IdentityAccess.Common;
using CleanArchitecture.Application.IdentityAccess.Organizations;
using CleanArchitecture.Application.IdentityAccess.Organizations.RegisterOrganization;
using CleanArchitecture.Application.IdentityAccess.Sessions;
using CleanArchitecture.Domain.IdentityAccess.Platform;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Application.IdentityAccess.Platform.Mfa;

/// <summary>
/// The one gate every MFA request passes, stated once (IA-REQ-041).
/// <para>
/// It is deliberately all four conditions together: an authenticated session, an identity whose address is
/// confirmed, a pending Platform invitation whose one-time token the caller submitted, and that invitation being
/// bound to this very identity. Dropping any one of them opens a different hole — token-only would let anyone
/// with the link enroll, session-only would let any signed-in user enroll for Platform, and matching by address
/// without the binding would let a second account claim the offer.
/// </para>
/// </summary>
internal static class PlatformMfaGate
{
    internal readonly record struct Admitted(PlatformAdminInvitation Invitation, Guid IdentityId, Guid SessionId);

    internal static async Task<Result<Admitted>> AdmitAsync(
        IApplicationDbContext context,
        ITokenHasher tokenHasher,
        IIdentityAccountService identities,
        ICurrentSession session,
        string token,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (session.IsInvalid || session.IdentityId is not { } identityId || session.SessionId is not { } sessionId)
        {
            return Result<Admitted>.Failure(IdentityAccessErrors.InvalidSession());
        }

        var identity = await identities.FindByIdAsync(identityId, cancellationToken);

        // An unconfirmed address is one nobody has proved they can read, so it cannot be the address a Platform
        // offer was made to. Refusing it here keeps confirmation an actual gate rather than a formality.
        if (identity is null || !identity.IsActive)
        {
            return Result<Admitted>.Failure(IdentityAccessErrors.InvalidInvitation());
        }

        var invitation = await PlatformInvitationDelivery.FindByTokenAsync(context, tokenHasher, token, cancellationToken);
        if (invitation is null ||
            !invitation.IsPendingAt(now) ||
            !invitation.IsAddressedTo(identity.Email) ||
            invitation.BoundIdentityId != identityId)
        {
            // One code for every way this can fail. Distinguishing them would tell a token holder about state
            // they were never shown (IA-REQ-029).
            return Result<Admitted>.Failure(IdentityAccessErrors.InvalidInvitation());
        }

        return Result<Admitted>.Success(new Admitted(invitation, identityId, sessionId.Value));
    }

    internal static Task<PlatformMfaEnrollment?> FindEnrollmentAsync(
        IApplicationDbContext context,
        Guid identityId,
        CancellationToken cancellationToken) =>
        context.PlatformMfaEnrollments
            .Include(enrollment => enrollment.RecoveryCodes)
            .SingleOrDefaultAsync(enrollment => enrollment.IdentityId == identityId, cancellationToken);
}

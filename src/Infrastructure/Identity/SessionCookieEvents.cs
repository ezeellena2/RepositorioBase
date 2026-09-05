using System.Security.Claims;
using CleanArchitecture.Domain.IdentityAccess.Sessions;
using CleanArchitecture.Domain.IdentityAccess.Memberships;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using CleanArchitecture.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CleanArchitecture.Infrastructure.Identity;

public sealed class SessionCookieEvents(TimeProvider timeProvider) : CookieAuthenticationEvents
{
    /// <summary>Name of the protected authentication cookie; it only references a persisted session.</summary>
    public const string CookieName = "__Host-ia-auth";

    public const string ValidatedSessionKey = "identity.validated-session";
    public const string AntiforgerySessionKey = "identity.antiforgery-session";
    public const string InvalidSessionKey = "identity.invalid-session";

    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        if (!TryReadSessionReference(context.Principal, out var identityId, out var sessionId))
        {
            await RejectAsync(context);
            return;
        }

        // This binds antiforgery to the protected ticket's session identifier; it is not an authentication decision.
        context.HttpContext.Items[AntiforgerySessionKey] = sessionId.Value;

        var database = context.HttpContext.RequestServices.GetRequiredService<ApplicationDbContext>();
        var user = await database.Users.SingleOrDefaultAsync(candidate => candidate.Id == identityId, context.HttpContext.RequestAborted);
        var now = timeProvider.GetUtcNow();
        if (user is null || !user.EmailConfirmed || IsLockedOut(user, now))
        {
            await RejectAsync(context);
            return;
        }

        var session = await database.UserSessions.Where(candidate => candidate.Id == sessionId && candidate.IdentityId == identityId).SingleOrDefaultAsync(context.HttpContext.RequestAborted);
        if (session is null || !session.IsActiveAt(now))
        {
            await RejectAsync(context);
            return;
        }

        if (session.ActiveTenantId is { } activeTenantId)
        {
            var hasActiveMembership = await (from membership in database.TenantMemberships
                                             join tenant in database.Tenants on membership.TenantId equals tenant.Id
                                             where membership.IdentityId == identityId && membership.TenantId == activeTenantId && membership.Status == MembershipStatus.Active && tenant.Status == TenantStatus.Active
                                             select membership).AnyAsync(context.HttpContext.RequestAborted);
            if (!hasActiveMembership)
            {
                session.ClearActiveTenant(now);
                try
                {
                    await database.SaveChangesAsync(context.HttpContext.RequestAborted);
                }
                catch (DbUpdateConcurrencyException)
                {
                    // Another request already changed this session, typically the same clearing issued by a parallel
                    // request. The committed row decides: continue only when it is still active.
                    await database.Entry(session).ReloadAsync(context.HttpContext.RequestAborted);
                    if (!session.IsActiveAt(now))
                    {
                        await RejectAsync(context);
                        return;
                    }
                }
            }
        }

        if (!await TouchAsync(database, session, now, context.HttpContext.RequestAborted))
        {
            await RejectAsync(context);
            return;
        }

        context.HttpContext.Items[ValidatedSessionKey] = new ValidatedSession(session.Id, session.IdentityId, user.Email!, session.ActiveTenantId, session.AbsoluteExpiresAt);
    }

    public override Task RedirectToLogin(RedirectContext<CookieAuthenticationOptions> context)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }

    public override Task RedirectToAccessDenied(RedirectContext<CookieAuthenticationOptions> context)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    }

    /// <summary>
    /// A protected ticket is only a reference to a persisted session. Anything other than exactly one
    /// non-empty identity identifier and one non-empty session identifier fails closed before any lookup,
    /// because <see cref="UserSessionId.From(Guid)"/> rejects an empty value and must never turn a
    /// malformed ticket into an unexpected failure.
    /// </summary>
    private static bool TryReadSessionReference(ClaimsPrincipal? principal, out Guid identityId, out UserSessionId sessionId)
    {
        identityId = Guid.Empty;
        sessionId = default;
        if (principal is null || principal.Claims.Count() != 2) return false;
        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out identityId) || identityId == Guid.Empty) return false;
        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.Sid), out var rawSessionId) || rawSessionId == Guid.Empty) return false;

        sessionId = UserSessionId.From(rawSessionId);
        return true;
    }

    /// <summary>
    /// Records activity with one atomic conditional update. PostgreSQL evaluates the liveness predicate under
    /// the row lock, so a revocation or expiry committed after the session was loaded yields zero rows and the
    /// request fails closed, while parallel requests of the same live session never conflict with each other.
    /// </summary>
    private static async Task<bool> TouchAsync(ApplicationDbContext database, UserSession session, DateTimeOffset now, CancellationToken cancellationToken)
    {
        session.Touch(now);
        var lastSeenAt = session.LastSeenAt;
        var idleExpiresAt = session.IdleExpiresAt;
        var affected = await database.UserSessions
            .Where(candidate => candidate.Id == session.Id && candidate.RevokedAt == null && candidate.IdleExpiresAt > now && candidate.AbsoluteExpiresAt > now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(candidate => candidate.LastSeenAt, candidate => candidate.LastSeenAt < lastSeenAt ? lastSeenAt : candidate.LastSeenAt)
                .SetProperty(candidate => candidate.IdleExpiresAt, candidate => candidate.IdleExpiresAt < idleExpiresAt ? idleExpiresAt : candidate.IdleExpiresAt), cancellationToken);
        if (affected == 0)
        {
            // The committed row is no longer live. Drop the in-memory touch so a later SaveChanges in this request
            // (public endpoints reuse the scoped context) can never resurrect or extend the session.
            database.Entry(session).State = EntityState.Detached;
            return false;
        }

        // The touch is persisted (or superseded by a later one); a later SaveChanges must never resend it.
        var entry = database.Entry(session);
        foreach (var property in new[] { entry.Property(candidate => candidate.LastSeenAt), entry.Property(candidate => candidate.IdleExpiresAt) })
        {
            property.OriginalValue = property.CurrentValue;
            property.IsModified = false;
        }

        return true;
    }

    private static bool IsLockedOut(ApplicationUser user, DateTimeOffset now) =>
        user.LockoutEnabled && user.LockoutEnd is { } lockoutEnd && lockoutEnd > now;

    /// <summary>
    /// Refuses the ticket and deletes the cookie carrying it.
    /// <para>
    /// Rejecting alone left the browser holding a cookie it could not use and could not get rid of: logout requires
    /// authentication, so an expired session never reached the handler that deletes it, and every later request
    /// still presented an invalid cookie, which public registration refuses. Deleting it ends that loop while
    /// conceding nothing — the principal is still rejected, this request is still anonymous, and no rejected
    /// session is ever treated as valid. The deletion goes through the same handler that issued the cookie, so it
    /// carries the identical <c>__Host-</c> attributes the browser requires before it will drop one.
    /// </para>
    /// <para>
    /// Every rejection deletes, including a lockout: a ticket refused on every request has no value while the
    /// refusal lasts, and keeping it would leave exactly the loop this closes — an unusable cookie that public
    /// registration then refuses. Signing in again is what ends a lockout for the person anyway.
    /// </para>
    /// </summary>
    private static async Task RejectAsync(CookieValidatePrincipalContext context)
    {
        // A rejected ticket must not influence anything else in the request, including antiforgery binding.
        context.HttpContext.Items.Remove(AntiforgerySessionKey);
        context.HttpContext.Items[InvalidSessionKey] = true;
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(context.Scheme.Name);
    }
}

public sealed record ValidatedSession(UserSessionId SessionId, Guid IdentityId, string Email, CleanArchitecture.Domain.IdentityAccess.Tenants.TenantId? ActiveTenantId, DateTimeOffset ExpiresAt);

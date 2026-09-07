using System.Globalization;
using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.IdentityAccess.Common;
using CleanArchitecture.Domain.IdentityAccess.Auditing;
using CleanArchitecture.Domain.IdentityAccess.ExternalLogins;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Application.IdentityAccess.ExternalLogins;

/// <summary>What the callback settled, and what the browser should be sent to next.</summary>
public sealed record ExternalCallbackOutcome(ExternalAuthorizationPurpose Purpose);

/// <summary>
/// The one write the provider callback performs.
/// <para>
/// It is deliberately not a business request. The callback is the protocol's own return leg: it carries the
/// provider's redirect rather than this application's antiforgery header or first-party `Origin`, so routing it
/// through a public handler would mean introducing a request anybody could reach without either. What it may do
/// is therefore narrowed to exactly this — copy what the middleware already validated onto a handoff that is
/// still live, or settle that handoff as failed — and every decision the copied facts lead to stays behind the
/// ordinary authorized requests that follow.
/// </para>
/// </summary>
public interface IExternalCallbackRecorder
{
    /// <summary>
    /// Records the validated assertion against a live handoff. Answers <see langword="null"/> when nothing here
    /// matches — an unknown, settled, expired or wrong-provider handoff is not an error to explain to a caller,
    /// because the caller is a redirect, not a person.
    /// </summary>
    /// <param name="authenticationTime">
    /// The provider's signed <c>auth_time</c>, exactly as the ID token carried it. A `Proof` that arrives without
    /// trustworthy evidence of a recent authentication is refused here (IA-REQ-051).
    /// </param>
    Task<ExternalCallbackOutcome?> RecordAsync(
        Guid handoffId,
        string provider,
        string subject,
        string? providerEmail,
        bool emailVerified,
        string? authenticationTime,
        CancellationToken cancellationToken);

    /// <summary>Settles a live handoff as failed, for a callback the protocol itself refused.</summary>
    Task<ExternalCallbackOutcome?> RejectAsync(Guid handoffId, CancellationToken cancellationToken);
}

public sealed class ExternalCallbackRecorder(
    IApplicationTransaction transaction,
    IApplicationDbContext context,
    TimeProvider timeProvider) : IExternalCallbackRecorder
{
    public Task<ExternalCallbackOutcome?> RecordAsync(
        Guid handoffId,
        string provider,
        string subject,
        string? providerEmail,
        bool emailVerified,
        string? authenticationTime,
        CancellationToken cancellationToken) =>
        transaction.ExecuteAsync(async ct =>
        {
            var handoff = await LiveAsync(handoffId, ct);

            // The provider is compared as well as the handoff, so a deployment offering two of them cannot have
            // one provider's assertion recorded against a challenge started for the other.
            if (handoff is null || !string.Equals(handoff.Provider, provider, StringComparison.Ordinal)) return null;

            var now = timeProvider.GetUtcNow();

            // A `Proof` has to prove somebody is here now. A token minted a second ago proves only that the
            // browser still holds a session at the provider, and asking for `prompt=login` proves nothing at all:
            // it is a request, and the reply that comes back never says whether it was honoured. The one fact
            // that distinguishes the two is the provider's own signed `auth_time`, so it is required — and
            // absent, unreadable, future-dated or stale are all refusals rather than a silent pass (IA-REQ-051).
            if (handoff.Purpose == ExternalAuthorizationPurpose.Proof
                && !ExternalAuthenticationEvidence.IsFresh(authenticationTime, now))
            {
                handoff.Fail(now);
                context.AuditEvents.Add(AuditEvent.CreateSessionEvent(
                    handoff.IdentityId,
                    handoff.SessionId?.Value,
                    "identity.external.proof.refused",
                    AuditCorrelation.Current(),
                    "authentication_evidence"));
                await context.SaveChangesAsync(ct);
                return null;
            }

            handoff.Validated(subject, providerEmail, emailVerified, now);
            await context.SaveChangesAsync(ct);
            return new ExternalCallbackOutcome(handoff.Purpose);
        }, cancellationToken);

    public Task<ExternalCallbackOutcome?> RejectAsync(Guid handoffId, CancellationToken cancellationToken) =>
        transaction.ExecuteAsync(async ct =>
        {
            var handoff = await LiveAsync(handoffId, ct);
            if (handoff is null) return null;
            handoff.Fail(timeProvider.GetUtcNow());
            await context.SaveChangesAsync(ct);
            return new ExternalCallbackOutcome(handoff.Purpose);
        }, cancellationToken);

    private async Task<ExternalAuthorizationRequest?> LiveAsync(Guid handoffId, CancellationToken cancellationToken)
    {
        var handoff = await context.ExternalAuthorizationRequests.SingleOrDefaultAsync(candidate => candidate.Id == handoffId, cancellationToken);
        return handoff?.IsLiveAt(timeProvider.GetUtcNow()) == true ? handoff : null;
    }
}

/// <summary>
/// What counts as signed evidence that the provider authenticated the person just now.
/// <para>
/// It lives beside its only caller rather than in Web, because the window is product policy about what a recent
/// proof means (IA-REQ-051) and not protocol wiring; and not in Domain, because no aggregate is party to it.
/// </para>
/// </summary>
public static class ExternalAuthenticationEvidence
{
    /// <summary>
    /// How recent the provider's own authentication must be. A **product default**, equal to the lifetime of the
    /// proof it is about to buy, so a proof can never outlive the evidence that bought it.
    /// </summary>
    public static readonly TimeSpan Freshness = TimeSpan.FromMinutes(5);

    /// <summary>Allowance for two clocks disagreeing, and for nothing else. A **product default**.</summary>
    public static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(1);

    /// <summary>
    /// The last second <see cref="DateTimeOffset.FromUnixTimeSeconds"/> accepts. Checked rather than caught:
    /// nothing a provider says may throw inside somebody else's transaction.
    /// </summary>
    private const long LatestRepresentable = 253_402_300_799;

    /// <summary>
    /// Fails closed in every direction. Missing evidence is not fresh evidence; neither is a value this system
    /// cannot read, nor one dated after now by more than two clocks can honestly differ.
    /// </summary>
    public static bool IsFresh(string? authenticationTime, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(authenticationTime)) return false;
        if (!long.TryParse(authenticationTime, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)) return false;
        if (seconds > LatestRepresentable) return false;

        var authenticatedAt = DateTimeOffset.FromUnixTimeSeconds(seconds);
        if (authenticatedAt > now + ClockSkew) return false;
        return now - authenticatedAt <= Freshness + ClockSkew;
    }
}

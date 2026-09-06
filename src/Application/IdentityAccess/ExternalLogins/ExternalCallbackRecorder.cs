using CleanArchitecture.Application.Common.Interfaces;
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
    Task<ExternalCallbackOutcome?> RecordAsync(
        Guid handoffId,
        string provider,
        string subject,
        string? providerEmail,
        bool emailVerified,
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
        CancellationToken cancellationToken) =>
        transaction.ExecuteAsync(async ct =>
        {
            var handoff = await LiveAsync(handoffId, ct);

            // The provider is compared as well as the handoff, so a deployment offering two of them cannot have
            // one provider's assertion recorded against a challenge started for the other.
            if (handoff is null || !string.Equals(handoff.Provider, provider, StringComparison.Ordinal)) return null;

            handoff.Validated(subject, providerEmail, emailVerified, timeProvider.GetUtcNow());
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

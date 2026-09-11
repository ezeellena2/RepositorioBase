using System.Globalization;
using System.Resources;
using CleanArchitecture.Application.Common.Localization;
using CleanArchitecture.Infrastructure.Data;
using CleanArchitecture.Infrastructure.Outbox;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Infrastructure.Localization;

/// <summary>Resource marker for the plain-text identity email catalog.</summary>
internal sealed class Emails;

/// <summary>
/// Resolves a delivery language from persisted state and renders with that culture explicitly. No ambient worker
/// or machine culture participates, and unsupported legacy values are skipped (IA-REQ-059).
/// </summary>
public sealed class IdentityEmailLocalizer(
    ApplicationDbContext context,
    ILookupNormalizer normalizer,
    LocalizationSettings settings)
{
    private static readonly ResourceManager Resources = new(typeof(Emails));

    public async Task<IdentityEmail> CreateAsync(
        string recipient,
        string? snapshotLanguage,
        string? boundLanguage,
        string subjectKey,
        string bodyKey,
        CancellationToken cancellationToken,
        params object[] arguments)
    {
        var language = await ResolveAsync(recipient, snapshotLanguage, boundLanguage, cancellationToken);
        var culture = CultureInfo.GetCultureInfo(language);
        var subject = Resources.GetString(subjectKey, culture)
            ?? throw new InvalidOperationException($"Email resource '{subjectKey}' is missing for '{language}'.");
        var bodyTemplate = Resources.GetString(bodyKey, culture)
            ?? throw new InvalidOperationException($"Email resource '{bodyKey}' is missing for '{language}'.");
        return new IdentityEmail(recipient, subject, string.Format(culture, bodyTemplate, arguments), language);
    }

    private async Task<string> ResolveAsync(
        string recipient,
        string? snapshotLanguage,
        string? boundLanguage,
        CancellationToken cancellationToken)
    {
        if (LocalizationRegistry.SupportedCanonicalOrNull(boundLanguage) is { } bound)
        {
            return bound;
        }

        var normalizedEmail = normalizer.NormalizeEmail(recipient);
        var accountLanguage = normalizedEmail is null
            ? null
            : await context.Users.AsNoTracking()
                .Where(user => user.NormalizedEmail == normalizedEmail)
                .Select(user => user.PreferredLanguage)
                .SingleOrDefaultAsync(cancellationToken);

        return LocalizationRegistry.SupportedCanonicalOrNull(accountLanguage)
            ?? LocalizationRegistry.SupportedCanonicalOrNull(snapshotLanguage)
            ?? settings.DefaultLanguage;
    }
}

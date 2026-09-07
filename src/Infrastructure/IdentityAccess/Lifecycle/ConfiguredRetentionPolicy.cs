using System.Globalization;
using System.Xml;
using CleanArchitecture.Application.IdentityAccess.Lifecycle;
using Microsoft.Extensions.Configuration;

namespace CleanArchitecture.Infrastructure.IdentityAccess.Lifecycle;

/// <summary>
/// Reads <c>IdentityAccess:RetentionPolicy</c> once, at startup (IA-REQ-056).
/// <para>
/// Every refusal here answers the same way: no policy. A missing section, a missing identifier, a category name
/// outside the closed set, a period that is not an ISO-8601 duration — all of them leave this returning
/// <see langword="null"/>, and a deployment with no policy performs no destructive action. That is the safe
/// direction to be wrong in, and it is the only direction this class can be wrong in.
/// </para>
/// <para>
/// Nothing here supplies a default period, a threshold or a jurisdictional number. A policy with a category that
/// names no period is a policy that has not been written yet, and the category is dropped rather than completed.
/// </para>
/// </summary>
public sealed class ConfiguredRetentionPolicy : IRetentionPolicy
{
    public ConfiguredRetentionPolicy(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        Current = Read(configuration.GetSection("IdentityAccess:RetentionPolicy"));
    }

    public RetentionPolicyDocument? Current { get; }

    private static RetentionPolicyDocument? Read(IConfigurationSection section)
    {
        if (!section.Exists()) return null;

        var policyId = Trimmed(section["PolicyId"]);
        var version = Trimmed(section["Version"]);
        var owner = Trimmed(section["Owner"]);
        var source = Trimmed(section["Source"]);
        if (policyId is null || version is null || owner is null || source is null) return null;
        if (!DateOnly.TryParse(section["ApprovedOn"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var approvedOn)) return null;

        var categories = section.GetSection("Categories").GetChildren()
            .Select(ReadCategory)
            .OfType<RetentionCategoryRule>()
            .ToArray();

        // A policy with nothing in it is a policy that says nothing, and a deployment that acts on it would be
        // acting on its own initiative.
        if (categories.Length == 0) return null;

        return new RetentionPolicyDocument(
            policyId, version, owner, approvedOn, source, Trimmed(section["BackupTreatment"]) ?? string.Empty, categories);
    }

    private static RetentionCategoryRule? ReadCategory(IConfigurationSection section)
    {
        if (!Enum.TryParse<RetentionCategory>(section["Category"], ignoreCase: false, out var category) || !Enum.IsDefined(category)) return null;
        if (!Enum.TryParse<RetentionTrigger>(section["Trigger"], ignoreCase: false, out var trigger) || !Enum.IsDefined(trigger)) return null;
        if (!Enum.TryParse<RetentionAction>(section["Action"], ignoreCase: false, out var action) || !Enum.IsDefined(action)) return null;

        var period = ReadPeriod(section["RetentionPeriod"]);
        if (period is not { } retentionPeriod) return null;

        return new RetentionCategoryRule(
            category,
            retentionPeriod,
            trigger,
            action,
            bool.TryParse(section["EvidenceRequired"], out var evidenceRequired) && evidenceRequired);
    }

    /// <summary>
    /// An ISO-8601 duration, because that is what a policy document writes and there is no reason to invent a
    /// second spelling for it. Anything else is not a period this system will act on.
    /// </summary>
    private static TimeSpan? ReadPeriod(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try
        {
            var period = XmlConvert.ToTimeSpan(value.Trim());
            return period > TimeSpan.Zero ? period : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

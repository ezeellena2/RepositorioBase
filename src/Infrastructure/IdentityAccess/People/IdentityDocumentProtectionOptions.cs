namespace CleanArchitecture.Infrastructure.IdentityAccess.People;

/// <summary>
/// The deployment's fingerprint key material, bound from <c>IdentityAccess:People:DocumentProtection</c>.
/// <para>
/// There is deliberately no default. A fingerprint produced under a key everybody knows is not keyed at all, and a
/// development default would silently become the production one; an unconfigured deployment refuses to record a
/// document instead (SPEC section 14.3).
/// </para>
/// </summary>
public sealed class IdentityDocumentProtectionOptions
{
    public const string SectionName = "IdentityAccess:People:DocumentProtection";

    /// <summary>The version new rows are written under. Every other configured version is retained for lookups.</summary>
    public int CurrentKeyVersion { get; set; }

    /// <summary>Base64 key material by version. Each key is at least 32 bytes.</summary>
    public Dictionary<int, string> FingerprintKeys { get; set; } = [];
}

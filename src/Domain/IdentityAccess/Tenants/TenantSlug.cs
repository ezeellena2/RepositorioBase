namespace CleanArchitecture.Domain.IdentityAccess.Tenants;

public readonly record struct TenantSlug
{
    private TenantSlug(string value) => Value = value;

    public string Value { get; }

    /// <summary>
    /// The one slug the Platform tenant may hold. It is reserved rather than merely conventional: the slug is
    /// unique, so naming it here is what stops an organization from taking it and what lets the singleton be
    /// found without a type scan (IA-REQ-039).
    /// </summary>
    public static TenantSlug Platform { get; } = From(ReservedPlatformSlug);

    internal const string ReservedPlatformSlug = "platform";

    public static TenantSlug From(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Tenant slugs cannot be empty.", nameof(value));
        }

        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-'))
        {
            throw new ArgumentException("Tenant slugs can contain only letters, digits, and hyphens.", nameof(value));
        }

        return new TenantSlug(normalized);
    }

    public override string ToString() => Value;
}

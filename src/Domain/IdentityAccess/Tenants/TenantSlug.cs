namespace CleanArchitecture.Domain.IdentityAccess.Tenants;

public readonly record struct TenantSlug
{
    private TenantSlug(string value) => Value = value;

    public string Value { get; }

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

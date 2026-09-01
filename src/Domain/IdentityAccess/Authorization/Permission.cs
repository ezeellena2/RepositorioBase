using CleanArchitecture.Domain.IdentityAccess.Tenants;

namespace CleanArchitecture.Domain.IdentityAccess.Authorization;

public sealed class Permission
{
    private Permission() { }

    public string Code { get; private set; } = string.Empty;

    public IReadOnlySet<TenantType> AllowedTenantTypes { get; private set; } = new HashSet<TenantType>();

    public static Permission Create(string code, IEnumerable<TenantType> allowedTenantTypes)
    {
        if (string.IsNullOrWhiteSpace(code) || code != code.Trim() || !IsResourceAction(code))
        {
            throw new ArgumentException("Permission codes must be normalized resource.action identifiers.", nameof(code));
        }

        ArgumentNullException.ThrowIfNull(allowedTenantTypes);
        var allowed = new HashSet<TenantType>(allowedTenantTypes);
        if (allowed.Count == 0)
        {
            throw new ArgumentException("Permissions must allow at least one tenant type.", nameof(allowedTenantTypes));
        }

        return new Permission
        {
            Code = code,
            AllowedTenantTypes = allowed
        };
    }

    private static bool IsResourceAction(string value)
    {
        var segments = value.Split('.', StringSplitOptions.None);
        return segments.Length >= 2 && segments.All(segment => segment.Length > 0 && segment.All(character => (character is >= 'a' and <= 'z') || char.IsAsciiDigit(character) || character == '-'));
    }
}

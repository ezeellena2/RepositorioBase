using CleanArchitecture.Domain.IdentityAccess.Tenants;

namespace CleanArchitecture.Domain.IdentityAccess.People;

/// <summary>
/// What a person may say about themselves. It is keyed by the identity, belongs to that identity's Personal tenant,
/// and carries only the two names self-service editing covers — never the email, never the ownership, never the
/// document (IA-REQ-050).
/// </summary>
public sealed class PersonProfile
{
    private PersonProfile()
    {
    }

    public Guid IdentityId { get; private set; }

    public TenantId PersonalTenantId { get; private set; }

    public string FullName { get; private set; } = string.Empty;

    public string DisplayName { get; private set; } = string.Empty;

    public DataClassification Classification { get; private set; }

    public static PersonProfile Create(Tenant personalTenant, Guid identityId, string fullName, string displayName, DataClassification classification)
    {
        ArgumentNullException.ThrowIfNull(personalTenant);
        if (personalTenant.Type != TenantType.Personal)
        {
            throw new InvalidOperationException("Person profiles require a personal tenant.");
        }

        if (personalTenant.Id.IsEmpty)
        {
            throw new ArgumentException("Tenant identifiers cannot be empty.", nameof(personalTenant));
        }

        if (identityId == Guid.Empty)
        {
            throw new ArgumentException("Identity identifiers cannot be empty.", nameof(identityId));
        }

        if (!Enum.IsDefined(classification)) throw new ArgumentOutOfRangeException(nameof(classification));
        EnsureNames(fullName, displayName);

        return new PersonProfile
        {
            IdentityId = identityId,
            PersonalTenantId = personalTenant.Id,
            FullName = fullName.Trim(),
            DisplayName = displayName.Trim(),
            Classification = classification
        };
    }

    /// <summary>Reports whether anything actually changed, so a no-op edit writes no audit record.</summary>
    public bool Rename(string fullName, string displayName)
    {
        EnsureNames(fullName, displayName);
        var trimmedFullName = fullName.Trim();
        var trimmedDisplayName = displayName.Trim();
        if (trimmedFullName == FullName && trimmedDisplayName == DisplayName) return false;

        FullName = trimmedFullName;
        DisplayName = trimmedDisplayName;
        return true;
    }

    private static void EnsureNames(string fullName, string displayName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
        {
            throw new ArgumentException("Full names cannot be empty.", nameof(fullName));
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("Display names cannot be empty.", nameof(displayName));
        }
    }
}

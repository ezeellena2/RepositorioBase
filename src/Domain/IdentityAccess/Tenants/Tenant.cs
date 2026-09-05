namespace CleanArchitecture.Domain.IdentityAccess.Tenants;

public sealed class Tenant : BaseEntity<TenantId>
{
    private Tenant()
    {
    }

    public TenantType Type { get; private set; }

    public TenantStatus Status { get; private set; }

    public TenantSlug Slug { get; private set; }

    public long AuthorizationVersion { get; private set; }

    /// <summary>Why this tenant is suspended, and when. Both are null unless it is.</summary>
    public TenantSuspensionReason? SuspensionReason { get; private set; }

    public DateTimeOffset? SuspendedAt { get; private set; }

    public static Tenant CreateOrganization(TenantSlug slug) => Create(TenantType.Organization, slug);

    public static Tenant CreatePersonal(TenantSlug slug) => Create(TenantType.Personal, slug);

    /// <summary>
    /// The single reserved Platform tenant (IA-REQ-039). Its slug is not a caller's choice: exactly one such
    /// tenant may exist, and the unique slug is what the database uses to hold that to one row.
    /// </summary>
    public static Tenant CreatePlatform() => Create(TenantType.Platform, TenantSlug.Platform);

    public void Activate()
    {
        EnsureStatus(TenantStatus.PendingConfirmation, "Only pending tenants can be activated.");
        Status = TenantStatus.Active;
        IncrementAuthorizationVersion();
    }

    /// <summary>
    /// Suspends the tenant with the reason it is being suspended for. The reason is required rather than
    /// optional because a suspension nobody recorded a cause for is one nobody can review or reverse with
    /// confidence (IA-REQ-043).
    /// </summary>
    public void Suspend(TenantSuspensionReason reason, DateTimeOffset now)
    {
        EnsurePlatformIsNotEndedHere("The Platform tenant cannot be suspended.");
        EnsureStatus(TenantStatus.Active, "Only active tenants can be suspended.");
        if (!Enum.IsDefined(reason))
        {
            throw new ArgumentOutOfRangeException(nameof(reason));
        }

        Status = TenantStatus.Suspended;
        SuspensionReason = reason;
        SuspendedAt = now;
        IncrementAuthorizationVersion();
    }

    /// <summary>
    /// Returns a suspended tenant to service and clears the evidence of the suspension it is leaving, so a
    /// reactivated tenant never reads as one that is still suspended for an old reason.
    /// </summary>
    public void Reactivate()
    {
        EnsureStatus(TenantStatus.Suspended, "Only suspended tenants can be reactivated.");
        Status = TenantStatus.Active;
        SuspensionReason = null;
        SuspendedAt = null;
        IncrementAuthorizationVersion();
    }

    public void Close()
    {
        EnsurePlatformIsNotEndedHere("The Platform tenant cannot be closed.");
        if (Status == TenantStatus.Closed)
        {
            throw new InvalidOperationException("Closed tenants cannot transition.");
        }

        Status = TenantStatus.Closed;
        IncrementAuthorizationVersion();
    }

    public void IncrementAuthorizationVersion() => AuthorizationVersion++;

    private static Tenant Create(TenantType type, TenantSlug slug)
    {
        if (string.IsNullOrWhiteSpace(slug.Value))
        {
            throw new ArgumentException("Tenant slugs cannot be empty.", nameof(slug));
        }

        return new Tenant
        {
            Id = TenantId.New(),
            Type = type,
            Status = TenantStatus.PendingConfirmation,
            Slug = slug,
            AuthorizationVersion = 0
        };
    }

    /// <summary>
    /// Platform runs the system that would have to carry out its own suspension. Ending it would leave nobody
    /// able to reverse that, so the refusal lives in the aggregate rather than only in the endpoints that
    /// happen to expose these transitions today (IA-REQ-043/046).
    /// </summary>
    private void EnsurePlatformIsNotEndedHere(string message)
    {
        if (Type == TenantType.Platform)
        {
            throw new InvalidOperationException(message);
        }
    }

    private void EnsureStatus(TenantStatus expectedStatus, string message)
    {
        if (Status != expectedStatus)
        {
            throw new InvalidOperationException(message);
        }
    }
}

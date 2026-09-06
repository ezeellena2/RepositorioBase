namespace CleanArchitecture.Infrastructure.IdentityAccess.Security;

/// <summary>
/// One fixed window of one attempt budget, shared by every instance of the deployment (IA-REQ-057).
/// <para>
/// It lives in Infrastructure rather than in the Domain because nothing about it is a business rule: it is the
/// storage the shared budget adapter reads and writes, and it is modelled as an entity only so the schema is
/// generated, migrated and asserted like every other table rather than conjured by raw SQL nobody tracks.
/// </para>
/// <para>
/// The key is stored only as a digest. A budget keyed by an address or an identity would otherwise turn an
/// operational table into a directory of who tried what (IA-REQ-029).
/// </para>
/// </summary>
public sealed class IdentityAttemptBudget
{
    private IdentityAttemptBudget()
    {
    }

    public string Scope { get; private set; } = string.Empty;

    public string KeyHash { get; private set; } = string.Empty;

    public DateTimeOffset WindowStart { get; private set; }

    public int Count { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }
}

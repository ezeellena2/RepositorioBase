using System.Text.Json;
using CleanArchitecture.Application.FunctionalTests.Infrastructure;
using CleanArchitecture.Application.IdentityAccess.Platform.Queries;
using CleanArchitecture.Domain.IdentityAccess.Organizations;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using CleanArchitecture.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;

namespace CleanArchitecture.Application.FunctionalTests.IdentityAccess.Platform;

/// <summary>
/// What the Platform directories may and may not contain (IA-REQ-044).
/// <para>
/// The positive assertions are exact field lists, and the negative ones are searches over the serialized payload
/// for values that must never appear. The negatives are the ones worth having: a projection can only leak what it
/// carries, and searching the whole response catches a leak through any field, including one added later.
/// </para>
/// </summary>
public sealed class PlatformProjectionTests : TestBase
{
    private const string Cuit = "30123456789";
    private const string LegalName = "Acme Sociedad Anónima";

    [Test]
    public async Task The_organization_projection_carries_exactly_the_allowlisted_fields()
    {
        await PlatformScenario.ActiveOwnerAsync();
        await OrganizationWithProfileAsync();

        var page = (await TestApp.SendAsync(new ListPlatformOrganizationsQuery(new PlatformDirectoryQuery(25, null)))).Value!;

        var item = page.Items.ShouldHaveSingleItem();
        Fields(item).ShouldBe([
            "AuthorizationVersion", "CreatedAtUtc", "Slug", "Status", "SuspendedAtUtc",
            "SuspensionReason", "TenantId", "Type", "UpdatedAtUtc"
        ]);
        item.Slug.ShouldStartWith("acme-");
        item.CreatedAtUtc.ShouldBeGreaterThan(DateTimeOffset.MinValue, "the operational timestamps are real values.");
        item.UpdatedAtUtc.ShouldBeGreaterThanOrEqualTo(item.CreatedAtUtc);
    }

    /// <summary>
    /// The business inside an organization is not Platform's to read. CUIT and legal name are the two things this
    /// system actually stores about one, so they are the two a leak would show up as.
    /// </summary>
    [Test]
    public async Task The_organization_projection_exposes_no_cuit_no_legal_name_and_no_profile_payload()
    {
        await PlatformScenario.ActiveOwnerAsync();
        await OrganizationWithProfileAsync();

        var payload = JsonSerializer.Serialize(
            (await TestApp.SendAsync(new ListPlatformOrganizationsQuery(new PlatformDirectoryQuery(25, null)))).Value);

        payload.ShouldNotContain(Cuit, Case.Insensitive);
        payload.ShouldNotContain("Acme Sociedad", Case.Insensitive);
        payload.ShouldNotContain("cuit", Case.Insensitive);
        payload.ShouldNotContain("legalName", Case.Insensitive);
    }

    [Test]
    public async Task The_identity_projection_carries_exactly_the_allowlisted_fields_and_no_credential()
    {
        var owner = await PlatformScenario.ActiveOwnerAsync();

        var page = (await TestApp.SendAsync(new ListPlatformIdentitiesQuery(new PlatformDirectoryQuery(25, null)))).Value!;

        var item = page.Items.ShouldHaveSingleItem();
        Fields(item).ShouldBe([
            "EmailConfirmed", "IdentityId", "IsLockedOut", "LastSeenUtc", "MembershipCount", "MfaStatus", "NormalizedEmail"
        ]);
        item.IdentityId.ShouldBe(owner.IdentityId);
        item.EmailConfirmed.ShouldBeTrue();
        item.MfaStatus.ShouldBe("Active");

        var payload = JsonSerializer.Serialize(page);
        payload.ShouldNotContain("passwordHash", Case.Insensitive);
        payload.ShouldNotContain("securityStamp", Case.Insensitive);
        payload.ShouldNotContain("concurrencyStamp", Case.Insensitive);
    }

    [Test]
    public async Task The_administrator_projection_carries_exactly_the_allowlisted_fields()
    {
        var owner = await PlatformScenario.ActiveOwnerAsync();

        var page = (await TestApp.SendAsync(new ListPlatformAdministratorsQuery(new PlatformDirectoryQuery(25, null)))).Value!;

        var item = page.Items.ShouldHaveSingleItem();
        Fields(item).ShouldBe([
            "EmailConfirmed", "IdentityId", "IsOwner", "MembershipId", "MembershipStatus", "MfaStatus",
            "NormalizedEmail", "SinceUtc"
        ]);
        item.IdentityId.ShouldBe(owner.IdentityId);
        item.IsOwner.ShouldBeTrue();
        item.SinceUtc.ShouldNotBeNull("the moment the invitation that granted it was consumed.");
    }

    /// <summary>
    /// An audit payload is arbitrary content, so only the two allowlisted keys leave the reader — and nothing a
    /// token, secret or address could have been written into does.
    /// </summary>
    [Test]
    public async Task The_audit_projection_carries_only_allowlisted_metadata_and_no_payload()
    {
        var owner = await PlatformScenario.ActiveOwnerAsync();

        var page = (await TestApp.SendAsync(new ListPlatformAuditQuery(new PlatformDirectoryQuery(100, null)))).Value!;

        page.Items.ShouldNotBeEmpty();
        Fields(page.Items[0]).ShouldBe([
            "ActorIdentityId", "CorrelationId", "EventId", "EventType", "OccurredAtUtc", "Outcome", "ReasonCode", "TenantId"
        ]);
        page.Items.ShouldContain(item => item.EventType == "platform.bootstrap.completed");

        var payload = JsonSerializer.Serialize(page);
        payload.ShouldNotContain("metadata", Case.Insensitive, "the stored payload is never handed over.");
        payload.ShouldNotContain(owner.Token, Case.Insensitive);
        payload.ShouldNotContain(owner.SharedKey, Case.Insensitive);
    }

    /// <summary>No recovery code, TOTP secret or invitation token may appear anywhere in any directory.</summary>
    [Test]
    public async Task No_directory_carries_a_secret_a_token_or_a_recovery_code()
    {
        var owner = await PlatformScenario.ActiveOwnerAsync();
        await OrganizationWithProfileAsync();

        var payloads = new[]
        {
            JsonSerializer.Serialize((await TestApp.SendAsync(new ListPlatformOrganizationsQuery(new PlatformDirectoryQuery(100, null)))).Value),
            JsonSerializer.Serialize((await TestApp.SendAsync(new ListPlatformIdentitiesQuery(new PlatformDirectoryQuery(100, null)))).Value),
            JsonSerializer.Serialize((await TestApp.SendAsync(new ListPlatformAdministratorsQuery(new PlatformDirectoryQuery(100, null)))).Value),
            JsonSerializer.Serialize((await TestApp.SendAsync(new ListPlatformAuditQuery(new PlatformDirectoryQuery(100, null)))).Value)
        };

        foreach (var payload in payloads)
        {
            payload.ShouldNotContain(owner.Token, Case.Insensitive);
            payload.ShouldNotContain(owner.SharedKey, Case.Insensitive);
            payload.ShouldNotContain("v1:", Case.Insensitive, "a versioned hash is still material nobody needs.");
            payload.ShouldNotContain(Cuit, Case.Insensitive);
        }
    }

    private static string[] Fields<T>(T item) =>
        typeof(T).GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray();

    private static async Task OrganizationWithProfileAsync()
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tenant = Tenant.CreateOrganization(TenantSlug.From($"acme-{Guid.NewGuid():N}"));
        tenant.Activate();
        context.Add(tenant);
        context.Add(OrganizationProfile.Create(tenant, LegalName, NormalizedCuit.From(Cuit)));
        await context.SaveChangesAsync();
    }
}

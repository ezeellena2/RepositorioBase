using CleanArchitecture.Application.FunctionalTests.Infrastructure;
using CleanArchitecture.Domain.IdentityAccess.Auditing;
using CleanArchitecture.Domain.IdentityAccess.Memberships;
using CleanArchitecture.Domain.IdentityAccess.Outbox;
using CleanArchitecture.Domain.IdentityAccess.Platform;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using CleanArchitecture.Infrastructure.Identity;

namespace CleanArchitecture.Application.FunctionalTests.IdentityAccess.Platform;

/// <summary>
/// The bootstrap ceremony (IA-REQ-040). It creates the singleton tenant, its system roles, one pending owner
/// invitation, one outbox intent and one audit decision — once — and it creates no identity, no password and no
/// membership at all.
/// </summary>
public sealed class PlatformBootstrapTests : TestBase
{
    private const string OwnerEmail = "platform-owner@example.test";

    [Test]
    public async Task Bootstrap_creates_the_singleton_tenant_its_roles_and_one_pending_owner_invitation()
    {
        (await PlatformScenario.BootstrapAsync(OwnerEmail)).ShouldBeTrue();

        var platform = (await TestApp.ListAsync<Tenant>()).ShouldHaveSingleItem();
        platform.Type.ShouldBe(TenantType.Platform);
        platform.Status.ShouldBe(TenantStatus.Active);
        platform.Slug.ShouldBe(TenantSlug.Platform);

        var invitation = await PlatformScenario.SingleInvitationAsync();
        invitation.IsOwner.ShouldBeTrue();
        invitation.Status.ShouldBe(PlatformAdminInvitationStatus.Pending);
        invitation.NormalizedEmail.ShouldBe(OwnerEmail);
        invitation.Language.ShouldBe("en", "bootstrap captures the configured default language");
        invitation.BoundIdentityId.ShouldBeNull("nobody has answered it yet.");

        (await TestApp.CountAsync<OutboxMessage>()).ShouldBe(1);
        (await TestApp.CountAsync<OutboxSecret>()).ShouldBe(1);
        (await TestApp.ListAsync<AuditEvent>()).ShouldContain(item => item.EventType == "platform.bootstrap.completed");
    }

    /// <summary>No default administrator and no default password: the invitation is the whole output (IA-REQ-032).</summary>
    [Test]
    public async Task Bootstrap_creates_no_identity_no_password_and_no_membership()
    {
        await PlatformScenario.BootstrapAsync(OwnerEmail);

        (await TestApp.CountAsync<ApplicationUser>()).ShouldBe(0);
        (await TestApp.CountAsync<TenantMembership>()).ShouldBe(0);
    }

    [Test]
    public async Task Running_the_ceremony_twice_creates_exactly_one_of_everything()
    {
        (await PlatformScenario.BootstrapAsync(OwnerEmail)).ShouldBeTrue();
        (await PlatformScenario.BootstrapAsync(OwnerEmail)).ShouldBeFalse("the tenant already exists.");

        (await TestApp.CountAsync<Tenant>()).ShouldBe(1);
        (await TestApp.CountAsync<PlatformAdminInvitation>()).ShouldBe(1);
        (await TestApp.CountAsync<OutboxMessage>()).ShouldBe(1);
        (await TestApp.ListAsync<AuditEvent>()).Count(item => item.EventType == "platform.bootstrap.completed").ShouldBe(1);
    }

    /// <summary>
    /// Configuration selects the first recipient and is never authority afterwards. Changing it once the tenant
    /// exists creates nothing — and after activation, nothing at all can be created from it (IA-REQ-040).
    /// </summary>
    [Test]
    public async Task Changing_the_configured_email_afterwards_creates_no_second_owner()
    {
        await PlatformScenario.BootstrapAsync(OwnerEmail);

        (await PlatformScenario.BootstrapAsync("someone.else@example.test")).ShouldBeFalse();

        (await TestApp.CountAsync<PlatformAdminInvitation>()).ShouldBe(1);
        (await PlatformScenario.SingleInvitationAsync()).NormalizedEmail.ShouldBe(OwnerEmail);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("not-an-address")]
    public async Task Missing_or_unusable_configuration_creates_nothing(string? configured)
    {
        (await PlatformScenario.BootstrapAsync(configured)).ShouldBeFalse();

        (await TestApp.CountAsync<Tenant>()).ShouldBe(0);
        (await TestApp.CountAsync<PlatformAdminInvitation>()).ShouldBe(0);
        (await TestApp.CountAsync<OutboxMessage>()).ShouldBe(0);
    }

    /// <summary>
    /// The whole ceremony is one transaction, so the token that reaches the recipient and the row it resolves to
    /// are always the same generation. Reading it back the way the dispatcher would is what proves the envelope
    /// carries a token the invitation actually accepts.
    /// </summary>
    [Test]
    public async Task The_sealed_token_resolves_to_the_invitation_the_ceremony_created()
    {
        await PlatformScenario.BootstrapAsync(OwnerEmail);

        var token = await PlatformScenario.PendingOwnerTokenAsync();
        var invitation = await PlatformScenario.SingleInvitationAsync();

        invitation.TokenHash.Matches(token).ShouldBeTrue();
        (await TestApp.ListAsync<OutboxMessage>()).Single().Payload.ShouldNotContain(token, Case.Insensitive);
    }
}

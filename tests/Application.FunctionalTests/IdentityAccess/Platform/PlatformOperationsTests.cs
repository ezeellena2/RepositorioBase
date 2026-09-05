using CleanArchitecture.Application.FunctionalTests.Infrastructure;
using CleanArchitecture.Application.IdentityAccess.Platform.Organizations;
using CleanArchitecture.Domain.IdentityAccess.Auditing;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using CleanArchitecture.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CleanArchitecture.Application.FunctionalTests.IdentityAccess.Platform;

/// <summary>
/// The Organization lifecycle Platform may drive (IA-REQ-043): reasoned, conditional, audited, immediately
/// effective, and unable to touch Platform itself.
/// </summary>
public sealed class PlatformOperationsTests : TestBase
{
    [Test]
    public async Task Suspending_an_organization_records_the_reason_and_moves_its_authorization_version()
    {
        await PlatformScenario.ActiveOwnerAsync();
        var (organizationId, versionBefore) = await OrganizationAsync();

        var result = await TestApp.SendAsync(new SuspendOrganizationTenantCommand(organizationId, TenantSuspensionReason.PolicyViolation));

        result.IsSuccess.ShouldBeTrue();
        var tenant = await TenantAsync(organizationId);
        tenant.Status.ShouldBe(TenantStatus.Suspended);
        tenant.SuspensionReason.ShouldBe(TenantSuspensionReason.PolicyViolation);
        tenant.SuspendedAt.ShouldNotBeNull();
        tenant.AuthorizationVersion.ShouldBeGreaterThan(versionBefore, "the evaluator has to see it at once.");
        (await TestApp.ListAsync<AuditEvent>()).ShouldContain(item => item.EventType == "platform.organization.suspended");
    }

    [Test]
    public async Task Reactivating_returns_it_to_service_and_clears_the_suspension_evidence()
    {
        await PlatformScenario.ActiveOwnerAsync();
        var (organizationId, _) = await OrganizationAsync();
        await TestApp.SendAsync(new SuspendOrganizationTenantCommand(organizationId, TenantSuspensionReason.BillingHold));

        var result = await TestApp.SendAsync(new ReactivateOrganizationTenantCommand(organizationId));

        result.IsSuccess.ShouldBeTrue();
        var tenant = await TenantAsync(organizationId);
        tenant.Status.ShouldBe(TenantStatus.Active);
        tenant.SuspensionReason.ShouldBeNull("a reactivated tenant must not read as one still suspended for an old reason.");
        tenant.SuspendedAt.ShouldBeNull();
        (await TestApp.ListAsync<AuditEvent>()).ShouldContain(item => item.EventType == "platform.organization.reactivated");
    }

    /// <summary>
    /// Ending Platform would leave nobody able to reverse it, and there is no higher authority to appeal to. The
    /// route refuses it, and so does the aggregate underneath (IA-REQ-043/046).
    /// </summary>
    [Test]
    public async Task Platform_itself_cannot_be_suspended_through_these_endpoints()
    {
        var owner = await PlatformScenario.ActiveOwnerAsync();

        var result = await TestApp.SendAsync(new SuspendOrganizationTenantCommand(owner.PlatformId.Value, TenantSuspensionReason.OperatorRequest));

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("invalid_platform_operation");
        (await TenantAsync(owner.PlatformId.Value)).Status.ShouldBe(TenantStatus.Active);
    }

    [Test]
    public async Task An_unknown_tenant_is_refused_and_nothing_is_written()
    {
        await PlatformScenario.ActiveOwnerAsync();

        var unknown = await TestApp.SendAsync(new SuspendOrganizationTenantCommand(Guid.NewGuid(), TenantSuspensionReason.OperatorRequest));
        var empty = await TestApp.SendAsync(new SuspendOrganizationTenantCommand(Guid.Empty, TenantSuspensionReason.OperatorRequest));

        unknown.Error!.Code.ShouldBe("invalid_platform_operation");
        empty.Error!.Code.ShouldBe(unknown.Error.Code);
        (await TestApp.ListAsync<AuditEvent>()).ShouldNotContain(item => item.EventType.StartsWith("platform.organization.", StringComparison.Ordinal));
    }

    [Test]
    public async Task A_suspension_without_a_valid_reason_is_refused()
    {
        await PlatformScenario.ActiveOwnerAsync();
        var (organizationId, _) = await OrganizationAsync();

        var result = await TestApp.SendAsync(new SuspendOrganizationTenantCommand(organizationId, (TenantSuspensionReason)99));

        result.IsFailure.ShouldBeTrue();
        (await TenantAsync(organizationId)).Status.ShouldBe(TenantStatus.Active);
    }

    /// <summary>Suspending twice, or reactivating what is not suspended, is a state error rather than a no-op.</summary>
    [Test]
    public async Task A_transition_that_does_not_apply_to_the_current_state_is_refused()
    {
        await PlatformScenario.ActiveOwnerAsync();
        var (organizationId, _) = await OrganizationAsync();

        (await TestApp.SendAsync(new ReactivateOrganizationTenantCommand(organizationId))).IsFailure.ShouldBeTrue();
        await TestApp.SendAsync(new SuspendOrganizationTenantCommand(organizationId, TenantSuspensionReason.SecurityIncident));
        (await TestApp.SendAsync(new SuspendOrganizationTenantCommand(organizationId, TenantSuspensionReason.SecurityIncident))).IsFailure.ShouldBeTrue();
    }

    [Test]
    public async Task Without_a_recent_step_up_a_lifecycle_change_is_refused()
    {
        await PlatformScenario.ActiveOwnerAsync();
        var (organizationId, _) = await OrganizationAsync();
        TestApp.SetSessionId(Guid.NewGuid());

        var result = await TestApp.SendAsync(new SuspendOrganizationTenantCommand(organizationId, TenantSuspensionReason.PolicyViolation));

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("recent_mfa_required");
        (await TenantAsync(organizationId)).Status.ShouldBe(TenantStatus.Active);
    }

    /// <summary>
    /// Two administrators acting at once must not silently overwrite each other. The write is conditional on the
    /// row's concurrency token, so the second is told the tenant moved and can re-decide (IA-REQ-035/043).
    /// </summary>
    [Test]
    public async Task A_stale_second_writer_is_told_the_tenant_moved_under_it()
    {
        await PlatformScenario.ActiveOwnerAsync();
        var (organizationId, _) = await OrganizationAsync();

        // Two requests read the same version; one of them settles first.
        var both = await Task.WhenAll(
            TestApp.SendAsync(new SuspendOrganizationTenantCommand(organizationId, TenantSuspensionReason.PolicyViolation)),
            TestApp.SendAsync(new SuspendOrganizationTenantCommand(organizationId, TenantSuspensionReason.BillingHold)));

        both.Count(result => result.IsSuccess).ShouldBe(1, "exactly one suspension persists.");
        var loser = both.Single(result => result.IsFailure);
        loser.Error!.Code.ShouldBeOneOf("platform_tenant_concurrency_conflict", "invalid_platform_operation");
        (await TenantAsync(organizationId)).Status.ShouldBe(TenantStatus.Suspended);
    }

    private static async Task<(Guid TenantId, long Version)> OrganizationAsync()
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tenant = Tenant.CreateOrganization(TenantSlug.From($"acme-{Guid.NewGuid():N}"));
        tenant.Activate();
        context.Add(tenant);
        await context.SaveChangesAsync();
        return (tenant.Id.Value, tenant.AuthorizationVersion);
    }

    private static async Task<Tenant> TenantAsync(Guid tenantId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.Tenants.AsNoTracking().SingleAsync(tenant => tenant.Id == TenantId.From(tenantId));
    }
}

using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.FunctionalTests.Infrastructure;
using CleanArchitecture.Application.IdentityAccess.Platform.Organizations;
using CleanArchitecture.Application.IdentityAccess.Platform.Queries;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using CleanArchitecture.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CleanArchitecture.Application.FunctionalTests.IdentityAccess.Platform;

/// <summary>
/// What a Platform session must have proved before it may read anything operational (IA-REQ-045).
/// <para>
/// The hole these close is not a missing permission. An activated administrator who signs out and back in with
/// only a password holds the same active Platform membership and the same read permissions they always did, and
/// sign-in selects that sole tenant for them — so tenant and permission both say yes. What their new session has
/// not done is prove the second factor, and until these tests existed nothing asked.
/// </para>
/// </summary>
public sealed class PlatformDirectoryAccessTests : TestBase
{
    [Test]
    public async Task A_password_only_session_reads_no_directory()
    {
        var owner = await PlatformScenario.ActiveOwnerAsync();

        // Exactly what signing in again produces: the same identity and tenant, a session that proved nothing.
        TestApp.SetSessionId(Guid.NewGuid());

        (await TestApp.SendAsync(new ListPlatformOrganizationsQuery(Page))).Error!.Code.ShouldBe("recent_mfa_required");
        (await TestApp.SendAsync(new ListPlatformIdentitiesQuery(Page))).Error!.Code.ShouldBe("recent_mfa_required");
        (await TestApp.SendAsync(new ListPlatformAdministratorsQuery(Page))).Error!.Code.ShouldBe("recent_mfa_required");
        (await TestApp.SendAsync(new ListPlatformAuditQuery(Page))).Error!.Code.ShouldBe("recent_mfa_required");
        owner.IdentityId.ShouldNotBe(Guid.Empty);
    }

    /// <summary>Proving the factor on this session is what opens them, and nothing else has to change.</summary>
    [Test]
    public async Task The_session_that_proved_the_factor_reads_every_directory()
    {
        await PlatformScenario.ActiveOwnerAsync();

        (await TestApp.SendAsync(new ListPlatformOrganizationsQuery(Page))).IsSuccess.ShouldBeTrue();
        (await TestApp.SendAsync(new ListPlatformIdentitiesQuery(Page))).IsSuccess.ShouldBeTrue();
        (await TestApp.SendAsync(new ListPlatformAdministratorsQuery(Page))).IsSuccess.ShouldBeTrue();
        (await TestApp.SendAsync(new ListPlatformAuditQuery(Page))).IsSuccess.ShouldBeTrue();
    }

    /// <summary>
    /// The thesis of the whole control, and the one thing a freshness check could not express.
    /// <para>
    /// Reading requires that this session proved the factor; changing requires that it did so recently. Wiring
    /// the read gate to the freshness window instead would pass every other test here and ship a panel that
    /// locks an administrator out of their own directories every fifteen minutes.
    /// </para>
    /// </summary>
    [Test]
    public async Task Proof_of_the_factor_outlives_the_freshness_a_change_requires()
    {
        var owner = await PlatformScenario.ActiveOwnerAsync();
        var (organizationId, _) = await OrganizationAsync();

        // The premise is elapsed time, which is all a step-up going stale is. Nothing else about the session,
        // the membership or the enrollment moves.
        await AgeTheStepUpAsync(owner.IdentityId, TimeSpan.FromHours(1));

        (await TestApp.SendAsync(new ListPlatformOrganizationsQuery(Page))).IsSuccess.ShouldBeTrue("proof does not expire.");
        (await TestApp.SendAsync(new ListPlatformAuditQuery(Page))).IsSuccess.ShouldBeTrue("proof does not expire.");

        var change = await TestApp.SendAsync(new SuspendOrganizationTenantCommand(organizationId.Value, TenantSuspensionReason.PolicyViolation));
        change.IsFailure.ShouldBeTrue();
        change.Error!.Code.ShouldBe("recent_mfa_required", "freshness is what a change requires, and it did expire.");
        (await TenantAsync(organizationId)).Status.ShouldBe(TenantStatus.Active);
    }

    /// <summary>
    /// Proving the factor for one session does not lend it to another. It is the same rule step-up already had,
    /// asked of reads: the evidence names the session that produced it.
    /// </summary>
    [Test]
    public async Task Proof_belongs_to_the_session_that_produced_it()
    {
        var owner = await PlatformScenario.ActiveOwnerAsync();
        (await TestApp.SendAsync(new ListPlatformOrganizationsQuery(Page))).IsSuccess.ShouldBeTrue();

        TestApp.SetSessionId(Guid.NewGuid());

        (await TestApp.SendAsync(new ListPlatformOrganizationsQuery(Page))).Error!.Code.ShouldBe("recent_mfa_required");
        owner.PlatformId.Value.ShouldNotBe(Guid.Empty);
    }

    private static PaginationQuery Page => new(1, 25);

    /// <summary>
    /// Moves the whole enrollment into the past, which is the only thing waiting does. Every timestamp shifts by
    /// the same interval on purpose: the row's lifecycle constraint requires the ceremony's own order to hold, so
    /// ageing one column alone would not be a session that grew stale — it would be a row that could never exist.
    /// </summary>
    private static async Task AgeTheStepUpAsync(Guid identityId, TimeSpan age)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var minutes = (int)age.TotalMinutes;
        await context.Database.ExecuteSqlAsync(
            $"""
            UPDATE "PlatformMfaEnrollments"
            SET "CreatedAt" = "CreatedAt" - make_interval(mins => {minutes}),
                "VerifiedAt" = "VerifiedAt" - make_interval(mins => {minutes}),
                "RecoveryAcknowledgedAt" = "RecoveryAcknowledgedAt" - make_interval(mins => {minutes}),
                "LastVerifiedAt" = "LastVerifiedAt" - make_interval(mins => {minutes})
            WHERE "IdentityId" = {identityId}
            """);
    }

    private static async Task<(TenantId Id, long Version)> OrganizationAsync()
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tenant = Tenant.CreateOrganization(TenantSlug.From($"acme-{Guid.NewGuid():N}"));
        tenant.Activate();
        context.Tenants.Add(tenant);
        await context.SaveChangesAsync();
        return (tenant.Id, tenant.AuthorizationVersion);
    }

    private static async Task<Tenant> TenantAsync(TenantId id)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.Tenants.SingleAsync(tenant => tenant.Id == id);
    }
}

using System.Net;
using CleanArchitecture.Application.FunctionalTests.IdentityAccess.Organizations;
using CleanArchitecture.Application.FunctionalTests.Infrastructure;
using CleanArchitecture.Application.IdentityAccess.Authorization;
using CleanArchitecture.Application.IdentityAccess.Credentials;
using CleanArchitecture.Domain.IdentityAccess.Auditing;
using CleanArchitecture.Domain.IdentityAccess.Authorization;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using NUnit.Framework;
using Shouldly;

namespace CleanArchitecture.Application.FunctionalTests.IdentityAccess.Members;

/// <summary>
/// What survives a membership change that fails, and what survives one that was never allowed (IA-REQ-026/053).
/// <para>
/// Two claims that only a real write can settle. A change is one transaction: rows that really landed are really
/// taken back, together with the audit record and the tenant's authorization version. A refusal is the opposite —
/// its evidence must outlive the request that was refused, which is why the denial writer has a scope of its own
/// and why a rolled-back business transaction cannot take that evidence with it.
/// </para>
/// </summary>
public sealed class MembershipAtomicityTests : TestBase
{
    [Test]
    public async Task A_failure_after_the_assignment_rows_land_takes_the_whole_change_back()
    {
        using var scenario = await OrganizationScenario.CreateAsync("atomic");
        var membershipId = await scenario.AddQuietMemberAsync("clerk", Permissions.MembersRead);
        var auditor = await CreateRoleAsync(scenario, "Auditor");
        var held = await RolesOfAsync(membershipId);
        var version = await AuthorizationVersionAsync(scenario);
        TestApp.ForceAssignmentRollbackAfterPersistedEffects();

        var failed = await AssignAsync(scenario, membershipId, [auditor]);

        failed.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        (await IdentityHttpHarness.ReadProblemAsync(failed)).GetProperty("code").GetString().ShouldBe("internal_server_error");
        (await failed.Content.ReadAsStringAsync()).Contains("rollback", StringComparison.OrdinalIgnoreCase)
            .ShouldBeFalse("a sanitized 500 says nothing about what actually threw (IA-REQ-029)");

        (await RolesOfAsync(membershipId)).ShouldBe(held, "the rows that landed were taken back with the transaction");
        (await AuthorizationVersionAsync(scenario)).ShouldBe(version, "an authorization version that moved would outlive a change that did not happen");
        (await TestApp.ListAsync<AuditEvent>())
            .Count(entry => entry.ActorId == scenario.OwnerId && entry.EventType == "membership.changed")
            .ShouldBe(0, "the actor-attributed record commits with the change or not at all (IA-REQ-027)");
    }

    /// <summary>The same change, unarmed, still works: the rollback left nothing behind to trip over.</summary>
    [Test]
    public async Task The_same_assignment_succeeds_once_the_injected_failure_is_gone()
    {
        using var scenario = await OrganizationScenario.CreateAsync("atomic");
        var membershipId = await scenario.AddQuietMemberAsync("clerk", Permissions.MembersRead);
        var auditor = await CreateRoleAsync(scenario, "Auditor");
        TestApp.ForceAssignmentRollbackAfterPersistedEffects();
        (await AssignAsync(scenario, membershipId, [auditor])).StatusCode.ShouldBe(HttpStatusCode.InternalServerError);

        var applied = await AssignAsync(scenario, membershipId, [auditor]);

        applied.StatusCode.ShouldBe(HttpStatusCode.OK, await applied.Content.ReadAsStringAsync());
        (await RolesOfAsync(membershipId)).ShouldBe([auditor]);
    }

    /// <summary>
    /// A refusal leaves evidence, and exactly the allowlisted evidence: the permission that was wanted and the
    /// reason it was not given. No target, no payload, nothing about the person being administered.
    /// </summary>
    [Test]
    public async Task A_refused_assignment_leaves_exactly_the_allowlisted_denial_evidence()
    {
        using var scenario = await OrganizationScenario.CreateAsync("atomic");
        var membershipId = await scenario.AddQuietMemberAsync("clerk", Permissions.MembersRead);
        var onlooker = await scenario.AddMemberAsync("onlooker", Permissions.MembersRead);
        await onlooker.Acting.ProveAsync(ProofActions.MemberRoleChange);

        var refused = await onlooker.Acting.SendAsync(
            HttpMethod.Put,
            $"/api/tenants/{scenario.TenantId.Value}/members/{membershipId}/roles",
            new { roleIds = Array.Empty<Guid>(), version = "1" });

        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var denied = (await TestApp.ListAsync<AuditEvent>())
            .Where(entry => entry.EventType == "authorization.denied" && entry.ActorId == onlooker.IdentityId)
            .ToArray();
        denied.ShouldHaveSingleItem().Metadata.ShouldBe(new Dictionary<string, string>
        {
            ["code"] = Permissions.MembersManage,
            ["outcome"] = "permission_denied"
        });
        denied[0].TenantId.ShouldBe(scenario.TenantId);
        denied[0].CorrelationId.ShouldNotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// The denial writer has its own scope, so evidence of a refusal cannot be undone by whatever the refused
    /// request was inside. Proved by refusing and then failing a real change in the same test: the denial is still
    /// there when the rolled-back change is not.
    /// </summary>
    [Test]
    public async Task Denial_evidence_outlives_a_business_transaction_that_rolls_back()
    {
        using var scenario = await OrganizationScenario.CreateAsync("atomic");
        var membershipId = await scenario.AddQuietMemberAsync("clerk", Permissions.MembersRead);
        var auditor = await CreateRoleAsync(scenario, "Auditor");
        var onlooker = await scenario.AddMemberAsync("onlooker", Permissions.MembersRead);
        var held = await RolesOfAsync(membershipId);
        await onlooker.Acting.ProveAsync(ProofActions.MemberRoleChange);
        (await onlooker.Acting.SendAsync(
            HttpMethod.Put,
            $"/api/tenants/{scenario.TenantId.Value}/members/{membershipId}/roles",
            new { roleIds = new[] { auditor }, version = "1" })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        TestApp.ForceAssignmentRollbackAfterPersistedEffects();
        (await AssignAsync(scenario, membershipId, [auditor])).StatusCode.ShouldBe(HttpStatusCode.InternalServerError);

        (await TestApp.ListAsync<AuditEvent>())
            .Count(entry => entry.EventType == "authorization.denied" && entry.ActorId == onlooker.IdentityId)
            .ShouldBe(1, "the refusal is recorded through an independent writer, so no later rollback erases it");
        (await RolesOfAsync(membershipId)).ShouldBe(held, "and the change that failed still left nothing behind");
    }

    private static async Task<HttpResponseMessage> AssignAsync(OrganizationScenario scenario, Guid membershipId, Guid[] roleIds)
    {
        await scenario.Owner.ProveAsync(ProofActions.MemberRoleChange);
        return await scenario.Owner.SendAsync(
            HttpMethod.Put,
            $"/api/tenants/{scenario.TenantId.Value}/members/{membershipId}/roles",
            new { roleIds, version = await VersionAsync(scenario, membershipId) });
    }

    private static async Task<Guid> CreateRoleAsync(OrganizationScenario scenario, string name)
    {
        await scenario.Owner.ProveAsync(ProofActions.RoleChange);
        var created = await scenario.Owner.SendAsync(
            HttpMethod.Post,
            $"/api/tenants/{scenario.TenantId.Value}/roles",
            new { name, permissions = new[] { Permissions.MembersRead } });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return (await IdentityHttpHarness.ReadJsonAsync(created)).GetProperty("roleId").GetGuid();
    }

    /// <summary>Read straight from the database, so a refused change cannot be hidden by a projection.</summary>
    private static async Task<Guid[]> RolesOfAsync(Guid membershipId) =>
        (await TestApp.ListAsync<MembershipRole>())
        .Where(link => link.MembershipId.Value == membershipId)
        .Select(link => link.RoleId.Value)
        .Order()
        .ToArray();

    private static async Task<long> AuthorizationVersionAsync(OrganizationScenario scenario) =>
        (await TestApp.ListAsync<Tenant>()).Single(tenant => tenant.Id == scenario.TenantId).AuthorizationVersion;

    private static async Task<string> VersionAsync(OrganizationScenario scenario, Guid membershipId) =>
        (await scenario.Owner.ReadAsync<MemberPageRow>($"/api/tenants/{scenario.TenantId.Value}/members"))
        .Items.Single(member => member.MembershipId == membershipId).Version;

    private sealed record MemberRow(Guid MembershipId, Guid IdentityId, string DisplayName, string NormalizedEmail, string Status, Guid[] RoleIds, bool IsOwner, string Version);

    private sealed record MemberPageRow(MemberRow[] Items);
}

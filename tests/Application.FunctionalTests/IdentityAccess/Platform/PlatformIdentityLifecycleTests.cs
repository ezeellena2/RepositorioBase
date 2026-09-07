using CleanArchitecture.Application.FunctionalTests.Infrastructure;
using CleanArchitecture.Application.IdentityAccess.Lifecycle;
using CleanArchitecture.Application.IdentityAccess.Platform.Identities;
using CleanArchitecture.Application.IdentityAccess.Platform.Queries;
using CleanArchitecture.Application.IdentityAccess.Sessions.CreateSession;
using CleanArchitecture.Domain.IdentityAccess.Auditing;
using CleanArchitecture.Domain.IdentityAccess.Identities;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using CleanArchitecture.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Shouldly;

namespace CleanArchitecture.Application.FunctionalTests.IdentityAccess.Platform;

/// <summary>
/// A Platform operator stopping an account and letting it go again (IA-REQ-054).
/// <para>
/// The distinction this file exists to hold: an administrative suspension and somebody parking their own account
/// are different states with different actors, and neither route reaches the other's. Lifting a suspension puts
/// the account back where it was, which is not always `Active`.
/// </para>
/// </summary>
public sealed class PlatformIdentityLifecycleTests : TestBase
{
    private const string Password = PlatformScenario.ValidPassword;

    [Test]
    public async Task An_operator_stops_an_account_and_it_loses_every_way_in()
    {
        await PlatformScenario.ActiveOwnerAsync();
        var (identityId, email) = await SignedInSubjectAsync();

        var suspended = await TestApp.SendAsync(new SuspendIdentityCommand(identityId, IdentitySuspensionReason.PolicyViolation, IdentityAccountStatus.Active));

        suspended.IsSuccess.ShouldBeTrue(suspended.Error?.Code);
        (await StatusAsync(identityId)).ShouldBe(IdentityAccountStatus.AdministrativelySuspended);
        (await LiveSessionCountAsync(identityId)).ShouldBe(0, "stopping an account reaches the sessions it already had");

        PlatformScenario.RunAnonymously();
        (await TestApp.SendAsync(new CreateSessionCommand(email, Password))).IsFailure
            .ShouldBeTrue("a suspended account does not answer its own password with a session");
    }

    /// <summary>
    /// The reason set is closed and lives in the audit alone. It is never a column and never a response, because
    /// "why was this person stopped" is an operational record, not an attribute of the person.
    /// </summary>
    [Test]
    public async Task Why_somebody_was_stopped_is_recorded_where_operators_read_it_and_nowhere_else()
    {
        await PlatformScenario.ActiveOwnerAsync();
        var (identityId, _) = await SignedInSubjectAsync();

        await TestApp.SendAsync(new SuspendIdentityCommand(identityId, IdentitySuspensionReason.SecurityIncident, IdentityAccountStatus.Active));

        var audited = (await TestApp.ListAsync<AuditEvent>())
            .Single(item => item.EventType == "identity.lifecycle.changed" && item.ActorId == identityId);
        audited.Metadata["outcome"].ShouldBe("administratively_suspended");
        audited.Metadata["reason"].ShouldBe(nameof(IdentitySuspensionReason.SecurityIncident));

        var directory = await TestApp.SendAsync(new ListPlatformIdentitiesQuery(new PlatformDirectoryQuery(25, null)));
        directory.IsSuccess.ShouldBeTrue();
        System.Text.Json.JsonSerializer.Serialize(directory.Value)
            .ShouldNotContain(nameof(IdentitySuspensionReason.SecurityIncident), Case.Insensitive);
    }

    /// <summary>
    /// Reactivation restores the state that preceded the disable, which for somebody who had parked their own
    /// account is that decision — not `Active`. An operator lifting their own suspension is not entitled to undo
    /// a choice that was never theirs.
    /// </summary>
    [Test]
    public async Task Lifting_a_suspension_puts_the_account_back_where_it_was_rather_than_where_it_is_convenient()
    {
        await PlatformScenario.ActiveOwnerAsync();
        var (identityId, email) = await SignedInSubjectAsync();
        await ParkAsync(identityId);

        await TestApp.SendAsync(new SuspendIdentityCommand(identityId, IdentitySuspensionReason.BillingHold, IdentityAccountStatus.SelfDeactivated));
        (await StatusAsync(identityId)).ShouldBe(IdentityAccountStatus.AdministrativelySuspended);

        var lifted = await TestApp.SendAsync(new ReactivateIdentityCommand(identityId, IdentityAccountStatus.AdministrativelySuspended, AcknowledgeSelfDeactivation: true));

        lifted.IsSuccess.ShouldBeTrue(lifted.Error?.Code);
        (await StatusAsync(identityId)).ShouldBe(IdentityAccountStatus.SelfDeactivated);
        PlatformScenario.RunAnonymously();
        (await TestApp.SendAsync(new CreateSessionCommand(email, Password))).IsFailure
            .ShouldBeTrue("the person's own decision outlives the operator's");
    }

    [Test]
    public async Task An_operator_who_has_not_read_that_the_person_parked_it_is_told_so_rather_than_surprised()
    {
        await PlatformScenario.ActiveOwnerAsync();
        var (identityId, _) = await SignedInSubjectAsync();
        await ParkAsync(identityId);
        await TestApp.SendAsync(new SuspendIdentityCommand(identityId, IdentitySuspensionReason.OperatorRequest, IdentityAccountStatus.SelfDeactivated));

        var refused = await TestApp.SendAsync(new ReactivateIdentityCommand(identityId, IdentityAccountStatus.AdministrativelySuspended, AcknowledgeSelfDeactivation: false));

        refused.IsFailure.ShouldBeTrue();
        refused.Error!.Code.ShouldBe("invalid_platform_operation");
        (await StatusAsync(identityId)).ShouldBe(IdentityAccountStatus.AdministrativelySuspended, "a refusal changes nothing");
    }

    [Test]
    public async Task Stopping_the_last_Platform_owner_would_leave_nobody_to_start_them_again()
    {
        var owner = await PlatformScenario.ActiveOwnerAsync();

        var refused = await TestApp.SendAsync(new SuspendIdentityCommand(owner.IdentityId, IdentitySuspensionReason.OperatorRequest, IdentityAccountStatus.Active));

        refused.IsFailure.ShouldBeTrue();
        refused.Error!.Code.ShouldBe("platform_last_owner");
        (await StatusAsync(owner.IdentityId)).ShouldBe(IdentityAccountStatus.Active);
    }

    /// <summary>
    /// The expected state is the precondition, not a hint. Two operators reading the same directory page and
    /// acting on it cannot both believe they were the one who moved it.
    /// </summary>
    [Test]
    public async Task A_change_that_expected_a_different_state_is_refused_against_the_state_that_won()
    {
        await PlatformScenario.ActiveOwnerAsync();
        var (identityId, _) = await SignedInSubjectAsync();
        await ParkAsync(identityId);

        var refused = await TestApp.SendAsync(new SuspendIdentityCommand(identityId, IdentitySuspensionReason.PolicyViolation, IdentityAccountStatus.Active));

        refused.IsFailure.ShouldBeTrue();
        refused.Error!.Code.ShouldBe("identity_concurrency_conflict");
        (await StatusAsync(identityId)).ShouldBe(IdentityAccountStatus.SelfDeactivated);
    }

    [Test]
    public async Task A_closed_account_is_a_tombstone_and_no_operator_may_reopen_it()
    {
        await PlatformScenario.ActiveOwnerAsync();
        var (identityId, _) = await SignedInSubjectAsync();
        await ForceStatusAsync(identityId, IdentityAccountStatus.Closed);

        var refused = await TestApp.SendAsync(new ReactivateIdentityCommand(identityId, IdentityAccountStatus.Closed, AcknowledgeSelfDeactivation: true));

        refused.IsFailure.ShouldBeTrue();
        refused.Error!.Code.ShouldBe("identity_reactivation_unavailable");
        (await StatusAsync(identityId)).ShouldBe(IdentityAccountStatus.Closed);
    }

    [Test]
    public async Task An_identity_nobody_has_is_answered_like_one_nobody_may_touch()
    {
        await PlatformScenario.ActiveOwnerAsync();

        var refused = await TestApp.SendAsync(new SuspendIdentityCommand(Guid.NewGuid(), IdentitySuspensionReason.PolicyViolation, IdentityAccountStatus.Active));

        refused.IsFailure.ShouldBeTrue();
        refused.Error!.Code.ShouldBe("not_found");
    }

    [Test]
    public async Task Stopping_an_account_is_not_something_a_session_may_do_because_it_once_proved_a_factor()
    {
        await PlatformScenario.ActiveOwnerAsync();
        var (identityId, _) = await SignedInSubjectAsync();
        TestApp.SetSessionId(Guid.NewGuid()); // A second session, which never stepped up.

        var refused = await TestApp.SendAsync(new SuspendIdentityCommand(identityId, IdentitySuspensionReason.PolicyViolation, IdentityAccountStatus.Active));

        refused.IsFailure.ShouldBeTrue();
        refused.Error!.Code.ShouldBe("recent_mfa_required");
        (await StatusAsync(identityId)).ShouldBe(IdentityAccountStatus.Active);
    }

    /// <summary>
    /// The guarantee that keeps the two halves apart: a ticket minted while somebody had parked their own account
    /// is not a way out of the suspension an operator imposed afterwards.
    /// </summary>
    [Test]
    public async Task A_ticket_from_before_the_suspension_does_not_survive_it()
    {
        await PlatformScenario.ActiveOwnerAsync();
        var (identityId, email) = await SignedInSubjectAsync();
        await ParkAsync(identityId);

        var operating = ActingContext.Capture();
        PlatformScenario.RunAnonymously();
        (await TestApp.SendAsync(new RequestAccountReactivationCommand(email))).IsSuccess.ShouldBeTrue();
        var ticket = await SealedReactivationTicketAsync();

        // Back as the operator, without walking the ceremony again: the step-up is bound to the session that
        // proved it, so restoring the context is the only way to still be the same operator.
        operating.Restore();
        (await TestApp.SendAsync(new SuspendIdentityCommand(identityId, IdentitySuspensionReason.SecurityIncident, IdentityAccountStatus.SelfDeactivated)))
            .IsSuccess.ShouldBeTrue();

        PlatformScenario.RunAnonymously();
        var spent = await TestApp.SendAsync(new ReactivateAccountCommand(ticket, Password));

        spent.IsFailure.ShouldBeTrue();
        spent.Error!.Code.ShouldBe("invalid_reactivation");
        (await StatusAsync(identityId)).ShouldBe(IdentityAccountStatus.AdministrativelySuspended);
    }

    /// <summary>A suspended account has no self-service way back, so asking for one enqueues nothing.</summary>
    [Test]
    public async Task A_suspended_account_asking_for_the_public_way_back_is_answered_and_sent_nothing()
    {
        await PlatformScenario.ActiveOwnerAsync();
        var (identityId, email) = await SignedInSubjectAsync();
        await TestApp.SendAsync(new SuspendIdentityCommand(identityId, IdentitySuspensionReason.PolicyViolation, IdentityAccountStatus.Active));

        PlatformScenario.RunAnonymously();
        var asked = await TestApp.SendAsync(new RequestAccountReactivationCommand(email));

        asked.IsSuccess.ShouldBeTrue("the answer does not vary by state");
        (await TestApp.ListAsync<CleanArchitecture.Domain.IdentityAccess.Outbox.OutboxMessage>())
            .ShouldNotContain(message => message.Type == "identity.reactivation.requested");
    }

    /// <summary>
    /// Who this test is acting as, so a step outside that identity can be taken and stepped back out of. The
    /// session id is part of it: Platform freshness is bound to the session that proved the factor, so an
    /// operator who came back with a different one is a different operator.
    /// </summary>
    private sealed record ActingContext(Guid? IdentityId, Guid? SessionId, TenantId? TenantId)
    {
        internal static ActingContext Capture() => new(TestApp.GetUserId(), TestApp.GetSessionId(), TestApp.GetTenantId());

        internal void Restore()
        {
            TestApp.SetUserId(IdentityId);
            TestApp.SetSessionId(SessionId);
            TestApp.SetCurrentTenant(TenantId);
            TestApp.SetApplicationPermissionGranted(true);
        }
    }

    private static async Task<(Guid IdentityId, string Email)> SignedInSubjectAsync()
    {
        var email = $"subject-{Guid.NewGuid():N}@example.test";
        var identityId = await IdentityHttpHarness.SeedConfirmedUserAsync(email, Password);

        var operating = ActingContext.Capture();
        PlatformScenario.RunAnonymously();
        (await TestApp.SendAsync(new CreateSessionCommand(email, Password))).IsSuccess.ShouldBeTrue();
        operating.Restore();
        return (identityId, email);
    }

    /// <summary>Parks the account the way the person would, without borrowing the operator's session for it.</summary>
    private static async Task ParkAsync(Guid identityId) => await ForceStatusAsync(identityId, IdentityAccountStatus.SelfDeactivated);

    private static async Task ForceStatusAsync(Guid identityId, IdentityAccountStatus status)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.Users.Where(user => user.Id == identityId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.Status, status));
    }

    private static async Task<IdentityAccountStatus> StatusAsync(Guid identityId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return (await context.Users.AsNoTracking().SingleAsync(user => user.Id == identityId)).Status;
    }

    private static async Task<int> LiveSessionCountAsync(Guid identityId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.UserSessions.CountAsync(session => session.IdentityId == identityId && session.RevokedAt == null);
    }

    private static async Task<string> SealedReactivationTicketAsync()
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var message = await context.OutboxMessages
            .Where(candidate => candidate.Type == "identity.reactivation.requested")
            .OrderByDescending(candidate => candidate.CreatedAt)
            .FirstAsync();
        var reader = scope.ServiceProvider.GetRequiredService<CleanArchitecture.Application.Common.Interfaces.IOutboxSecretReader>();
        return (await reader.ReadAsync(message.Id, CancellationToken.None))!;
    }
}

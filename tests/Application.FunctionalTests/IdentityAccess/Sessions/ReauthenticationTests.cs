using CleanArchitecture.Application.FunctionalTests.Infrastructure;
using CleanArchitecture.Application.IdentityAccess.Credentials;
using CleanArchitecture.Application.IdentityAccess.Credentials.Reauthenticate;
using CleanArchitecture.Domain.IdentityAccess.Sessions;
using CleanArchitecture.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Shouldly;

namespace CleanArchitecture.Application.FunctionalTests.IdentityAccess.Sessions;

/// <summary>
/// What a recent identity proof is and is not (IA-REQ-051). It is spendable once, by one session, for one action,
/// and only while nothing has changed about the credentials that produced it.
/// </summary>
public sealed class ReauthenticationTests : TestBase
{
    private const string Password = "Testing1234!";

    [Test]
    public async Task A_proof_is_spent_once_and_the_second_attempt_finds_nothing()
    {
        var (identityId, sessionId) = await SignedInAsync();

        (await TestApp.SendAsync(new ReauthenticateCommand(ProofActions.RevokeOneSession, Password))).IsSuccess.ShouldBeTrue();

        (await ConsumeAsync(identityId, sessionId, ProofActions.RevokeOneSession)).ShouldBeTrue();
        (await ConsumeAsync(identityId, sessionId, ProofActions.RevokeOneSession))
            .ShouldBeFalse("a proof is not a password: spending it is what makes it gone");
    }

    [Test]
    public async Task A_proof_made_on_one_session_authorizes_nothing_from_another()
    {
        var (identityId, sessionId) = await SignedInAsync();
        var elsewhere = await SeedSessionAsync(identityId);

        (await TestApp.SendAsync(new ReauthenticateCommand(ProofActions.RevokeOtherSessions, Password))).IsSuccess.ShouldBeTrue();

        (await ConsumeAsync(identityId, elsewhere, ProofActions.RevokeOtherSessions))
            .ShouldBeFalse("proving on a laptop must not authorize a change made from somewhere else");
        (await ConsumeAsync(identityId, sessionId, ProofActions.RevokeOtherSessions)).ShouldBeTrue();
    }

    [Test]
    public async Task A_proof_for_one_action_authorizes_no_other()
    {
        var (identityId, sessionId) = await SignedInAsync();

        (await TestApp.SendAsync(new ReauthenticateCommand(ProofActions.ExternalUnlink, Password))).IsSuccess.ShouldBeTrue();

        (await ConsumeAsync(identityId, sessionId, ProofActions.PasswordChange))
            .ShouldBeFalse("proving in order to unlink a provider is not permission to change a password");
        (await ConsumeAsync(identityId, sessionId, ProofActions.ExternalUnlink)).ShouldBeTrue();
    }

    [Test]
    public async Task A_wrong_password_and_an_action_this_system_does_not_know_answer_the_same_way()
    {
        var (identityId, sessionId) = await SignedInAsync();

        var wrongPassword = await TestApp.SendAsync(new ReauthenticateCommand(ProofActions.PasswordChange, "Wrong1234!"));
        var unknownAction = await TestApp.SendAsync(new ReauthenticateCommand("something.else", Password));

        wrongPassword.IsSuccess.ShouldBeFalse();
        unknownAction.IsSuccess.ShouldBeFalse();
        unknownAction.Error!.Code.ShouldBe(wrongPassword.Error!.Code, "which one it was is not state the caller was shown");
        wrongPassword.Error!.Code.ShouldBe("invalid_credential_proof");
        (await ConsumeAsync(identityId, sessionId, ProofActions.PasswordChange)).ShouldBeFalse("nothing was issued");
    }

    [Test]
    public async Task A_proof_that_outlived_its_window_is_refused()
    {
        var (identityId, sessionId) = await SignedInAsync();
        (await TestApp.SendAsync(new ReauthenticateCommand(ProofActions.RevokeOneSession, Password))).IsSuccess.ShouldBeTrue();

        await AgeProofsAsync(identityId, TimeSpan.FromMinutes(10));

        (await ConsumeAsync(identityId, sessionId, ProofActions.RevokeOneSession))
            .ShouldBeFalse("five minutes is the window; waiting it out is not a way through");
    }

    [Test]
    public async Task Anything_that_changes_a_credential_invalidates_every_outstanding_proof()
    {
        var (identityId, sessionId) = await SignedInAsync();
        (await TestApp.SendAsync(new ReauthenticateCommand(ProofActions.PasswordChange, Password))).IsSuccess.ShouldBeTrue();
        (await TestApp.SendAsync(new ReauthenticateCommand(ProofActions.ExternalLink, Password))).IsSuccess.ShouldBeTrue();

        await AdvanceVersionAsync(identityId);

        (await ConsumeAsync(identityId, sessionId, ProofActions.PasswordChange)).ShouldBeFalse();
        (await ConsumeAsync(identityId, sessionId, ProofActions.ExternalLink))
            .ShouldBeFalse("one version, every proof: a compromise does not have to be cleaned up proof by proof");
    }

    [Test]
    public async Task Proving_twice_leaves_one_proof_rather_than_a_spare_to_spend_later()
    {
        var (identityId, sessionId) = await SignedInAsync();

        (await TestApp.SendAsync(new ReauthenticateCommand(ProofActions.RevokeOneSession, Password))).IsSuccess.ShouldBeTrue();
        (await TestApp.SendAsync(new ReauthenticateCommand(ProofActions.RevokeOneSession, Password))).IsSuccess.ShouldBeTrue();

        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await context.RecentIdentityProofs.CountAsync(proof => proof.IdentityId == identityId && proof.ConsumedAt == null))
            .ShouldBe(1, "a fresh proof replaces the live one rather than stacking on it");

        (await ConsumeAsync(identityId, sessionId, ProofActions.RevokeOneSession)).ShouldBeTrue();
        (await ConsumeAsync(identityId, sessionId, ProofActions.RevokeOneSession)).ShouldBeFalse();
    }

    [Test]
    public async Task A_proof_never_leaves_the_server()
    {
        var (identityId, _) = await SignedInAsync();
        (await TestApp.SendAsync(new ReauthenticateCommand(ProofActions.PasswordChange, Password))).IsSuccess.ShouldBeTrue();

        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var proof = await context.RecentIdentityProofs.SingleAsync(candidate => candidate.IdentityId == identityId);

        // There is no token column and no secret to hand out: the record is addressed by what the request already
        // proves about itself, so there is nothing for a caller to hold, copy or replay.
        context.Entry(proof).Properties.Select(property => property.Metadata.Name)
            .ShouldNotContain("Token");
        TestApp.CapturedLogs.ShouldNotContain(entry => entry.Contains(Password, StringComparison.Ordinal));
    }

    private static async Task<(Guid IdentityId, UserSessionId SessionId)> SignedInAsync()
    {
        var identityId = await IdentityHttpHarness.SeedConfirmedUserAsync($"proof-{Guid.NewGuid():N}@example.test", Password);
        var sessionId = await SeedSessionAsync(identityId);
        TestApp.SetUserId(identityId);
        TestApp.SetSessionId(sessionId.Value);
        TestApp.SetApplicationPermissionGranted(true);
        return (identityId, sessionId);
    }

    private static async Task<UserSessionId> SeedSessionAsync(Guid identityId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var session = UserSession.Create(identityId, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(30), TimeSpan.FromHours(12));
        context.UserSessions.Add(session);
        await context.SaveChangesAsync();
        return session.Id;
    }

    private static async Task<bool> ConsumeAsync(Guid identityId, UserSessionId sessionId, string action)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IRecentIdentityProofStore>();
        return await store.TryConsumeAsync(identityId, sessionId, action, CancellationToken.None);
    }

    private static async Task AdvanceVersionAsync(Guid identityId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IRecentIdentityProofStore>();
        await store.AdvanceVersionAsync(identityId, CancellationToken.None);
    }

    /// <summary>Moves the window into the past, which is the only thing waiting out an expiry does.</summary>
    private static async Task AgeProofsAsync(Guid identityId, TimeSpan by)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.RecentIdentityProofs
            .Where(proof => proof.IdentityId == identityId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(proof => proof.IssuedAt, proof => proof.IssuedAt - by)
                .SetProperty(proof => proof.ExpiresAt, proof => proof.ExpiresAt - by));
    }
}

using System.Text.RegularExpressions;

namespace CleanArchitecture.Web.AcceptanceTests.StepDefinitions;

/// <summary>
/// The Platform journeys, driven through the browser.
/// <para>
/// Nothing about the owner is seeded. The application performs the bootstrap ceremony as it starts, from the
/// address the run configured; the invitation and the confirmation are read out of the mail the dispatcher
/// actually delivered to a local drop folder; and the address is confirmed through the same screen a person uses.
/// The tokens are sealed with keys held by the application process, so reading the delivered mail is the only way
/// to follow a real link — and it is also what a person does.
/// </para>
/// <para>
/// A deployment is bootstrapped once, so the ceremony is walked once per run: the first scenario that needs an
/// owner walks it, recording what it saw at each gate, and every scenario reads that record. Each scenario
/// starting its own ceremony is not an option — it would need the invitation the one before it consumed, and the
/// only way to hand it one is to seed the authority the ceremony exists to create.
/// </para>
/// </summary>
[Binding]
public sealed class PlatformOperationsStepDefinitions(ScenarioContext scenario)
{
    private static IBrowserContext? featureContext;
    private static IPage? featurePage;
    private static Task<OwnerWalk>? theWalk;

    private static IPage Page => featurePage ?? throw new InvalidOperationException("The Platform feature has no page.");

    private static IdentitySignInPage SignIn => new(Page);
    private static PlatformRecoveryPage Recovery => new(Page);
    private static PlatformInvitationPages Invitation => new(Page);
    private static PlatformOperationsPage Panel => new(Page);

    /// <summary>
    /// One browser for the whole feature, because it is one person. The session the ceremony ends with is the
    /// session the panel scenarios operate from, which is what someone who just finished enrolling actually has.
    /// </summary>
    [BeforeFeature("PlatformOperations")]
    public static async Task BeforePlatformFeature()
    {
        featureContext = await PlaywrightSetup.NewContextAsync();
        featurePage = await featureContext.NewPageAsync();
    }

    [AfterFeature("PlatformOperations")]
    public static async Task AfterPlatformFeature()
    {
        theWalk = null;
        if (featureContext is not null)
        {
            await featureContext.DisposeAsync();
            featureContext = null;
            featurePage = null;
        }
    }

    [Given("the first owner has been walked in from the deployment's cold start")]
    public async Task GivenTheOwnerHasBeenWalkedIn()
    {
        // A faulted walk stays faulted, so every scenario reports the one real failure rather than a second,
        // meaningless one caused by the ceremony the first never finished.
        scenario.Set(await (theWalk ??= WalkTheCeremonyAsync()), "walk");
        await Panel.GotoAsync();
    }

    [Then("the cold start had left one Platform tenant, one pending invitation for the configured address and no membership")]
    public void ThenTheColdStartLeftAnInvitationAndNothingElse()
    {
        var walk = scenario.Get<OwnerWalk>("walk");
        walk.PlatformTenants.ShouldBe(1);
        walk.PendingOwnerInvitations.ShouldBe(1);
        walk.InvitedAddress.ShouldBe(AspireSetup.PlatformBootstrapOwnerEmail);
        // The ceremony creates no identity, no password and no membership — only an invitation.
        walk.MembershipsAtColdStart.ShouldBe(0);
    }

    [Then("no visitor had been offered the panel")]
    public void ThenNoVisitorWasOfferedThePanel() =>
        scenario.Get<OwnerWalk>("walk").PanelOfferedToVisitor.ShouldBeFalse();

    [Then("a failed delivery had been resent without naming anybody, leaving one invitation pending")]
    public void ThenTheResendNamedNobody()
    {
        var walk = scenario.Get<OwnerWalk>("walk");
        walk.ResendAcknowledgement.ShouldContain("If an owner invitation is waiting");
        walk.ResendAcknowledgement.ShouldNotContain("@");
        walk.PendingOwnerInvitationsAfterResend.ShouldBe(1);
    }

    [Then("the invitation had arrived, and choosing a password had granted no membership")]
    public void ThenChoosingAPasswordGrantedNothing()
    {
        var walk = scenario.Get<OwnerWalk>("walk");
        walk.InvitationPath.ShouldBe("/platform/invitations/register");
        walk.MembershipsAfterChoosingAPassword.ShouldBe(0);
    }

    [Then("answering the invitation again had reissued the confirmation, and only the newest link had confirmed the address")]
    public void ThenTheReissuedConfirmationWasTheOneThatWorked() =>
        scenario.Get<OwnerWalk>("walk").ConfirmationPath.ShouldBe("/platform/invitations/confirm");

    [Then("signing in had granted no membership either, and the panel was still refused")]
    public void ThenSigningInGrantedNothing()
    {
        var walk = scenario.Get<OwnerWalk>("walk");
        walk.MembershipsAfterSigningIn.ShouldBe(0);
        walk.PanelOfferedBeforeSecondFactor.ShouldBeFalse();
    }

    [Then("the invitation link they still held had carried them into the second factor")]
    public void ThenTheInvitationLinkCarriedThemOn() =>
        scenario.Get<OwnerWalk>("walk").ContinuationPath.ShouldBe("/platform/invitations/register");

    [Then("only the second factor had granted the membership")]
    public void ThenTheSecondFactorGrantedTheMembership() =>
        scenario.Get<OwnerWalk>("walk").MembershipsAfterSecondFactor.ShouldBe(1);

    [Then("the Platform panel is offered to them")]
    public async Task ThenThePanelIsOffered()
    {
        await Panel.GotoAsync();
        await Panel.AssertOfferedAsync();
    }

    [Given("an active organization exists")]
    public async Task GivenAnActiveOrganizationExists()
    {
        var slug = $"acme-{Guid.NewGuid():N}";
        await PlatformFixtures.ActiveOrganizationAsync(slug);
        scenario.Set(slug, "slug");
        await Panel.GotoAsync();
    }

    [When("they step up and suspend that organization")]
    public async Task WhenTheySuspend()
    {
        await Panel.StepUpAsync(scenario.Get<OwnerWalk>("walk").SharedKey);
        await Panel.SuspendAsync(scenario.Get<string>("slug"), "SecurityIncident");
    }

    [Then("the organization is suspended with the reason they gave")]
    public async Task ThenTheOrganizationIsSuspended() =>
        (await PlatformFixtures.OrganizationStatusAsync(scenario.Get<string>("slug")))
            .ShouldBe("Suspended:SecurityIncident");

    [Then("the suspension appears in the Platform audit")]
    public async Task ThenTheSuspensionIsAudited()
    {
        await Panel.GotoAsync();
        await Panel.AssertAuditContainsAsync("platform.organization.suspended");
    }

    [When("they reactivate it")]
    public async Task WhenTheyReactivate()
    {
        await Panel.GotoAsync();
        await Panel.StepUpAsync(scenario.Get<OwnerWalk>("walk").SharedKey);
        await Panel.ReactivateAsync(scenario.Get<string>("slug"));
    }

    [Then("the organization is active again")]
    public async Task ThenTheOrganizationIsActive() =>
        (await PlatformFixtures.OrganizationStatusAsync(scenario.Get<string>("slug"))).ShouldBe("Active:-");

    [Then("the panel offers no impersonation, deletion or context override")]
    public Task ThenNoProhibitedCapability() => Panel.AssertNoProhibitedCapabilityAsync();

    /// <summary>
    /// Operating Platform and deciding who operates it are different authorities. The owner holds both, which is
    /// why the form is offered here; that an administrator holds only the first is asserted where the grants are
    /// made, in <c>PlatformRoleGrantTests</c> (IA-REQ-042).
    /// </summary>
    [Then("the owner is the one offered the administrator invitation")]
    public Task ThenTheOwnerMayInvite() => Panel.AssertCanInviteAdministratorAsync();

    // ---- the two branches the browser had never walked (Task 28) ----------------------------------------

    [When("the owner invites another administrator")]
    public async Task WhenTheOwnerInvitesAnotherAdministrator()
    {
        var invitee = $"platform-admin-{Guid.NewGuid():N}@example.test";
        scenario.Set(invitee, "invitee");
        await Panel.GotoAsync();
        await Panel.InviteAdministratorAsync(invitee, scenario.Get<OwnerWalk>("walk").SharedKey);
    }

    /// <summary>
    /// The same gates the owner met, walked by somebody who was invited rather than bootstrapped — and in a
    /// browser of their own, because they are a different person and a shared jar would make them the owner.
    /// </summary>
    [When("that administrator answers the delivered invitation, confirms, signs in and proves a second factor")]
    public async Task WhenTheAdministratorWalksTheGates()
    {
        var invitee = scenario.Get<string>("invitee");
        await using var context = await PlaywrightSetup.NewContextAsync();
        var page = await context.NewPageAsync();
        var invitation = new PlatformInvitationPages(page);

        var delivered = await PlatformFixtures.DeliveredAsync(invitee, "invited to Platform");
        await invitation.OpenDeliveredAsync(delivered);
        await invitation.RegisterAsync(PlatformFixtures.Password);
        await invitation.AssertNeutralAcknowledgementAsync();

        var confirmation = await PlatformFixtures.DeliveredAsync(invitee, "Confirm your Platform address");
        await invitation.OpenDeliveredAsync(confirmation);
        await invitation.ConfirmAsync();

        await new IdentitySignInPage(page).GotoAsync();
        await new IdentitySignInPage(page).SignInAsync(invitee, PlatformFixtures.Password);

        // Back to the invitation, which is where the last gate is — the same continuation the owner took, and
        // the same reason: the ceremony is bound to the offer rather than to whoever is signed in.
        await invitation.OpenDeliveredAsync(delivered);
        await invitation.ContinueToSecondFactorAsync();
        await invitation.CompleteMfaAsync();

        var panel = new PlatformOperationsPage(page);
        await panel.GotoAsync();
        await panel.AssertOfferedAsync();
        scenario.Set(await PlatformFixtures.PlatformMembershipsAsync(), "memberships");
    }

    [Then("Platform holds two memberships and the panel is offered to the new administrator")]
    public void ThenPlatformHoldsTwoMemberships() =>
        scenario.Get<long>("memberships").ShouldBe(2, "the owner and the administrator they invited, and nobody else");

    [Given("an identity that already has an account of its own")]
    public async Task GivenAnIdentityThatAlreadyHasAnAccount()
    {
        var identity = await IdentityAccessFixtures.ConfirmedIdentityAsync();
        scenario.Set(identity.Email, "invitee");
    }

    [When("the owner invites that identity to Platform")]
    public async Task WhenTheOwnerInvitesThatIdentity()
    {
        await Panel.GotoAsync();
        await Panel.InviteAdministratorAsync(scenario.Get<string>("invitee"), scenario.Get<OwnerWalk>("walk").SharedKey);
    }

    /// <summary>
    /// The branch that matters here: an invitee who already has an identity submits a password, and the one they
    /// already had is the one that still works. A route that took the submitted one would be a way to replace
    /// anybody's password by inviting their address to Platform.
    /// </summary>
    [When("it answers the invitation with a different password")]
    public async Task WhenItAnswersWithADifferentPassword()
    {
        var invitee = scenario.Get<string>("invitee");
        await using var context = await PlaywrightSetup.NewContextAsync();
        var page = await context.NewPageAsync();
        var invitation = new PlatformInvitationPages(page);

        var delivered = await PlatformFixtures.DeliveredAsync(invitee, "invited to Platform");
        await invitation.OpenDeliveredAsync(delivered);
        await invitation.RegisterAsync("SomethingElse4Platform!");
        await invitation.AssertNeutralAcknowledgementAsync();
    }

    [Then("the password it already had is the one that still signs it in")]
    public async Task ThenTheOriginalPasswordStillWorks()
    {
        var invitee = scenario.Get<string>("invitee");
        await using var context = await PlaywrightSetup.NewContextAsync();
        var page = await context.NewPageAsync();
        var signIn = new IdentitySignInPage(page);

        await signIn.GotoAsync();
        var refused = await signIn.AttemptSignInAsync(invitee, "SomethingElse4Platform!");
        refused.Status.ShouldBe(204, "a refused sign-in is neutral, not an error");
        refused.Headers.ContainsKey("set-cookie").ShouldBeFalse("the password the invitation carried was never adopted");

        await signIn.GotoAsync();
        await signIn.SignInAsync(invitee, IdentityAccessFixtures.Password);
    }

    /// <summary>
    /// The reproduction of R3, as a person performs it. Nothing is seeded and nothing is fabricated: the "Log out"
    /// link in the navigation, the real sign-in form, and the panel's own step-up.
    /// </summary>
    [When("they sign out and sign in again with their password")]
    public async Task WhenTheySignOutAndBackIn()
    {
        await Panel.GotoAsync();
        await SignIn.SignOutAsync();
        await SignIn.GotoAsync();
        await SignIn.SignInAsync(AspireSetup.PlatformBootstrapOwnerEmail, PlatformFixtures.Password);
    }

    [Then("the panel asks for the second factor and shows no directory")]
    public async Task ThenThePanelAsksForTheSecondFactor()
    {
        await Panel.GotoAsync();
        await Panel.AssertStepUpRequestedAsync();
    }

    [When("they prove the second factor on the panel")]
    public async Task WhenTheyProveTheSecondFactor() =>
        await Panel.StepUpAsync(scenario.Get<OwnerWalk>("walk").SharedKey);

    /// <summary>
    /// The whole ceremony, once, in the order a first owner meets it: a cold start, a delivery that failed and was
    /// recovered, the invitation, the confirmation, the sign-in and the second factor. Each gate is read rather
    /// than judged here, so the scenario that reports a reading is also the place that says what it had to be.
    /// </summary>
    private static async Task<OwnerWalk> WalkTheCeremonyAsync()
    {
        var owner = AspireSetup.PlatformBootstrapOwnerEmail;

        // Waiting for the ceremony's rows is waiting for the deployment to be ready, which is what a first owner
        // waits for too.
        await PlatformFixtures.PlatformTenantIdAsync();
        var tenants = await PlatformFixtures.CountAsync("SELECT count(*) FROM \"Tenants\" WHERE \"Type\" = 'Platform';");
        var pending = await PlatformFixtures.PendingOwnerInvitationsAsync();
        var invited = await PlatformFixtures.ScalarAsync(
            "SELECT \"NormalizedEmail\" FROM \"PlatformAdminInvitations\" WHERE \"IsOwner\" LIMIT 1;");
        var membershipsAtColdStart = await PlatformFixtures.PlatformMembershipsAsync();

        // The panel is a URL like any other, so typing it must not be a way in.
        await Panel.GotoAsync();
        var offeredToVisitor = await PanelIsOfferedAsync();

        // The first delivery is waited for before it is failed, because recovery rotates the token: the mail that
        // matters afterwards is the one that was not in the folder before, and this is what names the difference.
        var firstDelivery = await PlatformFixtures.DeliveredAsync(owner, "invited to Platform");
        await PlatformFixtures.FailOwnerDeliveryAsync();
        await Recovery.GotoAsync();
        await Recovery.AssertAcceptsNoRecipientAsync();
        await Recovery.ResendAsync();
        await Recovery.AssertNeutralAcknowledgementAsync();
        var acknowledgement = await Page.GetByRole(AriaRole.Status).InnerTextAsync();
        var pendingAfterResend = await PlatformFixtures.PendingOwnerInvitationsAsync();

        var invitation = await PlatformFixtures.DeliveredAsync(owner, "invited to Platform", [firstDelivery.DropFile]);
        await Invitation.OpenDeliveredAsync(invitation);
        await Invitation.RegisterAsync(PlatformFixtures.Password);
        await Invitation.AssertNeutralAcknowledgementAsync();
        var membershipsAfterPassword = await PlatformFixtures.PlatformMembershipsAsync();

        var confirmation = await PlatformFixtures.DeliveredAsync(owner, "Confirm your Platform address");

        // Answering the invitation again before confirming, which is what someone whose confirmation expired or
        // was lost would do. It reissues the confirmation and retires the one it replaces, so the recipient ends
        // up with exactly one link that works — and this is the only place in the suite where the superseded
        // envelope has actually been delivered, which is the state the retire has to cover.
        await Invitation.OpenDeliveredAsync(invitation);
        await Invitation.RegisterAsync(PlatformFixtures.Password);
        await Invitation.AssertNeutralAcknowledgementAsync();
        var reissued = await PlatformFixtures.DeliveredAsync(owner, "Confirm your Platform address", [confirmation.DropFile]);

        await Invitation.OpenDeliveredAsync(confirmation);
        await Invitation.AssertConfirmationRefusedAsync();

        await Invitation.OpenDeliveredAsync(reissued);
        await Invitation.ConfirmAsync();

        await SignIn.GotoAsync();
        await SignIn.SignInAsync(owner, PlatformFixtures.Password);
        var membershipsAfterSignIn = await PlatformFixtures.PlatformMembershipsAsync();

        await Panel.GotoAsync();
        var offeredBeforeSecondFactor = await PanelIsOfferedAsync();

        // Back to the invitation mail, which is where the last gate is. The ceremony is bound to the offer rather
        // than to whoever happens to be signed in, so it needs the invitation token — and the only place that
        // token exists is the mail and the page that reads it. Opening that link again and taking the control it
        // offers is the whole continuation; the walk composes no URL of its own.
        await Invitation.OpenDeliveredAsync(invitation);
        var continuationPath = new Uri(Page.Url).AbsolutePath;
        await Invitation.ContinueToSecondFactorAsync();
        await Invitation.CompleteMfaAsync();
        var sharedKey = await Invitation.ReadSharedKeyAsync();
        var membershipsAfterSecondFactor = await PlatformFixtures.PlatformMembershipsAsync();

        return new OwnerWalk(
            tenants,
            pending,
            invited,
            membershipsAtColdStart,
            offeredToVisitor,
            acknowledgement,
            pendingAfterResend,
            invitation.Path,
            membershipsAfterPassword,
            confirmation.Path,
            membershipsAfterSignIn,
            offeredBeforeSecondFactor,
            continuationPath,
            membershipsAfterSecondFactor,
            sharedKey);
    }

    /// <summary>
    /// Reads whether the panel answered rather than asserting which way it answered. It waits for whichever of the
    /// four possible screens arrived — the directories, the refusal, the step-up it asks for, or the sign-in page
    /// so the reading is of a settled page rather than of a race.
    /// </summary>
    private static async Task<bool> PanelIsOfferedAsync()
    {
        var offered = Page.GetByRole(AriaRole.Heading, new() { Name = "Organizations" });
        var refused = Page.GetByText(new Regex("MFA-authenticated Platform administrator"));
        var stepUp = Page.GetByRole(AriaRole.Form, new() { Name = "Step up" });
        var visitor = Page.GetByRole(AriaRole.Heading, new() { Name = "Sign in" });
        await Assertions.Expect(offered.Or(refused).Or(stepUp).Or(visitor)).ToBeVisibleAsync();
        return await offered.CountAsync() == 1;
    }

    /// <summary>
    /// What the one walk saw, gate by gate. It holds readings rather than verdicts: the scenario is where a
    /// reading is said to be right or wrong, and the scenario is the only part a person reads.
    /// </summary>
    internal sealed record OwnerWalk(
        long PlatformTenants,
        long PendingOwnerInvitations,
        string? InvitedAddress,
        long MembershipsAtColdStart,
        bool PanelOfferedToVisitor,
        string ResendAcknowledgement,
        long PendingOwnerInvitationsAfterResend,
        string InvitationPath,
        long MembershipsAfterChoosingAPassword,
        string ConfirmationPath,
        long MembershipsAfterSigningIn,
        bool PanelOfferedBeforeSecondFactor,
        string ContinuationPath,
        long MembershipsAfterSecondFactor,
        string SharedKey);
}

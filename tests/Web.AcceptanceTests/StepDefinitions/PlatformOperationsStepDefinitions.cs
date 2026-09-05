namespace CleanArchitecture.Web.AcceptanceTests.StepDefinitions;

/// <summary>
/// The Platform journeys, driven through the browser.
/// <para>
/// The bootstrap ceremony is not seeded: the application performs it as it starts, from the address the run
/// configured, and the first scenario asserts what it left behind. What is seeded is a later administrator's
/// invitation, because the token the application mints leaves the process only inside an envelope this one holds
/// no key for. Everything a scenario asserts still happens through the browser.
/// </para>
/// </summary>
[Binding]
public sealed class PlatformOperationsStepDefinitions(ScenarioContext scenario)
{
    private static IBrowserContext? featureContext;
    private static IPage? sharedPage;

    private static IPage Page => sharedPage ?? throw new InvalidOperationException("The Platform feature has no page.");

    private IdentitySignInPage SignIn => new(Page);
    private PlatformRecoveryPage Recovery => new(Page);
    private PlatformInvitationPages Invitation => new(Page);
    private PlatformOperationsPage Panel => new(Page);

    [BeforeScenario("PlatformOperations")]
    public static async Task BeforePlatformScenario()
    {
        featureContext = await PlaywrightSetup.NewContextAsync();
        sharedPage = await featureContext.NewPageAsync();
    }

    [AfterScenario("PlatformOperations")]
    public static async Task AfterPlatformScenario()
    {
        if (featureContext is not null)
        {
            await featureContext.DisposeAsync();
            featureContext = null;
            sharedPage = null;
        }
    }

    [Given("the application has started with a configured Platform owner")]
    public async Task GivenTheApplicationHasBootstrapped() =>
        scenario.Set(await PlatformFixtures.PlatformTenantIdAsync(), "platform");

    [Then("exactly one Platform tenant and one pending owner invitation exist")]
    public async Task ThenOneTenantAndOnePendingOwner()
    {
        (await PlatformFixtures.CountAsync("SELECT count(*) FROM \"Tenants\" WHERE \"Type\" = 'Platform';")).ShouldBe(1);
        (await PlatformFixtures.PendingOwnerInvitationsAsync()).ShouldBe(1);
        (await PlatformFixtures.ScalarAsync(
            "SELECT \"NormalizedEmail\" FROM \"PlatformAdminInvitations\" WHERE \"IsOwner\" LIMIT 1;"))
            .ShouldBe(AspireSetup.PlatformBootstrapOwnerEmail);
    }

    /// <summary>The ceremony creates no identity, no password and no membership — only an invitation.</summary>
    [Then("no Platform membership exists")]
    public async Task ThenNoPlatformMembership() =>
        (await PlatformFixtures.PlatformMembershipsAsync()).ShouldBe(0);

    [Then("the Platform panel is not offered to a visitor")]
    public async Task ThenThePanelIsNotOfferedToAVisitor()
    {
        await Panel.GotoAsync();
        await SignIn.AssertVisibleAsync();
    }

    [Given("the owner invitation could not be delivered")]
    public Task GivenTheOwnerInvitationFailed() => PlatformFixtures.FailOwnerDeliveryAsync();

    [When("anyone asks for it to be resent")]
    public async Task WhenAnyoneAsksForAResend()
    {
        await Recovery.GotoAsync();
        await Recovery.AssertAcceptsNoRecipientAsync();
        await Recovery.ResendAsync();
    }

    [Then("the answer says nothing about who it was for")]
    public Task ThenTheAnswerIsNeutral() => Recovery.AssertNeutralAcknowledgementAsync();

    [Then("exactly one Platform owner invitation is still pending")]
    public async Task ThenOneOwnerInvitationIsPending() =>
        (await PlatformFixtures.PendingOwnerInvitationsAsync()).ShouldBe(1);

    [Given("a Platform administrator has been invited")]
    public async Task GivenAnAdministratorHasBeenInvited()
    {
        await PlatformFixtures.PlatformTenantIdAsync();
        var (email, token) = await PlatformFixtures.AdministratorInvitationAsync();
        scenario.Set(email, "email");
        scenario.Set(token, "token");
    }

    [When("they register with the invitation and a password they chose")]
    public async Task WhenTheyRegister()
    {
        await Invitation.GotoRegisterAsync(scenario.Get<string>("token"));
        await Invitation.RegisterAsync(PlatformFixtures.Password);
        await Invitation.AssertNeutralAcknowledgementAsync();
    }

    [Then("they hold no Platform membership yet")]
    public async Task ThenTheyHoldNoMembershipYet() =>
        (await PlatformFixtures.PlatformMembershipsAsync()).ShouldBe(0);

    [When("they confirm their address and sign in")]
    public async Task WhenTheyConfirmAndSignIn()
    {
        await PlatformFixtures.ConfirmAsync(scenario.Get<string>("email"));
        await SignIn.GotoAsync();
        await SignIn.SignInAsync(scenario.Get<string>("email"), PlatformFixtures.Password);
    }

    [Then("the Platform panel is not offered to them")]
    public async Task ThenThePanelIsNotOffered()
    {
        await Panel.GotoAsync();
        await Panel.AssertNotOfferedAsync();
    }

    [When("they complete the second factor and acknowledge their recovery codes")]
    public async Task WhenTheyCompleteTheSecondFactor()
    {
        await Invitation.GotoMfaAsync(scenario.Get<string>("token"));
        scenario.Set(await SharedKeyThroughCeremonyAsync(), "sharedKey");
    }

    [Then("the Platform panel is offered to them")]
    public async Task ThenThePanelIsOffered()
    {
        await Panel.GotoAsync();
        await Panel.AssertOfferedAsync();
    }

    [Given("a Platform administrator has completed every gate")]
    public async Task GivenAnAdministratorHasCompletedEveryGate()
    {
        await GivenAnAdministratorHasBeenInvited();
        await WhenTheyRegister();
        await WhenTheyConfirmAndSignIn();
        await WhenTheyCompleteTheSecondFactor();
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
        await Panel.StepUpAsync(scenario.Get<string>("sharedKey"));
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
        await Panel.StepUpAsync(scenario.Get<string>("sharedKey"));
        await Panel.ReactivateAsync(scenario.Get<string>("slug"));
    }

    [Then("the organization is active again")]
    public async Task ThenTheOrganizationIsActive() =>
        (await PlatformFixtures.OrganizationStatusAsync(scenario.Get<string>("slug"))).ShouldBe("Active:-");

    [Then("the panel offers no impersonation, deletion or context override")]
    public Task ThenNoProhibitedCapability() => Panel.AssertNoProhibitedCapabilityAsync();

    /// <summary>
    /// Operating Platform and deciding who operates it are different authorities: an administrator holds the first
    /// and only an owner holds the second (IA-REQ-042).
    /// </summary>
    [Then("an administrator cannot invite another administrator")]
    public Task ThenAnAdministratorCannotInvite() => Panel.AssertCannotInviteAdministratorAsync();

    private async Task<string> SharedKeyThroughCeremonyAsync()
    {
        await Invitation.CompleteMfaAsync();
        return scenario.TryGetValue<string>("sharedKey", out var existing) ? existing : await ReadKeyAsync();
    }

    /// <summary>
    /// The key is read back from the page that showed it, because the ceremony hands it over exactly once and a
    /// later step-up has to present a code from the same secret.
    /// </summary>
    private async Task<string> ReadKeyAsync() => await Invitation.ReadSharedKeyAsync();
}

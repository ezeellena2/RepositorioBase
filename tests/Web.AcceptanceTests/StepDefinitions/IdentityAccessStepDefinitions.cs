using System.Net.Http.Json;
using System.Text.Json;

namespace CleanArchitecture.Web.AcceptanceTests.StepDefinitions;

/// <summary>
/// The identity journeys, driven through the browser. Setup is seeded and the assertions are what a person would
/// see: which organization is active, which actions are offered, whether a page is reachable at all.
/// </summary>
[Binding]
public sealed class IdentityAccessStepDefinitions(ScenarioContext scenario)
{
    private static IBrowserContext? featureContext;
    private static IPage? sharedPage;

    private IdentitySignInPage SignIn => new(Page);
    private RegisterOrganizationPage Register => new(Page);
    private IdentityContextPage Context => new(Page);
    private TenantSelectorPage Tenants => new(Page);
    private InvitationPages Invitations => new(Page);
    private InviteMemberPage Invite => new(Page);
    private ConfirmEmailPage Confirmation => new(Page);

    private static IPage Page => sharedPage ?? throw new InvalidOperationException("The identity feature has no page.");

    private IdentityAccessFixtures.SeededIdentity Identity
    {
        get => scenario.Get<IdentityAccessFixtures.SeededIdentity>("identity");
        set => scenario.Set(value, "identity");
    }

    private IdentityAccessFixtures.SeededOrganization Organization
    {
        get => scenario.Get<IdentityAccessFixtures.SeededOrganization>("organization");
        set => scenario.Set(value, "organization");
    }

    [BeforeScenario("IdentityAccess")]
    public static async Task BeforeIdentityScenario()
    {
        // A context per scenario, because these journeys are about what one browser remembers: a shared jar
        // would let one scenario's session decide another's outcome.
        featureContext = await PlaywrightSetup.NewContextAsync();
        sharedPage = await featureContext.NewPageAsync();
    }

    [AfterScenario("IdentityAccess")]
    public static async Task AfterIdentityScenario()
    {
        if (featureContext is not null)
        {
            await featureContext.DisposeAsync();
            featureContext = null;
            sharedPage = null;
        }
    }

    [Given("a visitor registers an organization")]
    public async Task GivenAVisitorRegistersAnOrganization()
    {
        var email = $"founder-{Guid.NewGuid():N}@example.test";
        scenario.Set(email, "email");
        await Register.GotoAsync();
        await Register.RegisterAsync($"Acceptance {Guid.NewGuid():N}", IdentityAccessFixtures.NextCuit(), email, IdentityAccessFixtures.Password);
    }

    [Then("the registration answers neutrally without revealing whether the address was taken")]
    public Task ThenTheRegistrationIsNeutral() => Register.AssertNeutralAcknowledgementAsync();

    [Then("the organization is not usable before its confirmation")]
    public async Task ThenTheOrganizationIsNotUsable()
    {
        await Context.GotoAsync();
        await SignIn.AssertVisibleAsync();
    }

    [When("the invitee opens the delivered confirmation link and confirms")]
    public async Task WhenTheInviteeConfirms() => await ConfirmThroughDeliveredMailAsync(scenario.Get<string>("email"));

    [Then("the confirmation link left no token in the address bar")]
    public Task ThenTheConfirmationLinkLeftNoToken() => Confirmation.AssertFragmentClearedAsync();

    [Then("they can sign in and reach their access page")]
    public async Task ThenTheyCanSignIn()
    {
        await SignIn.GotoAsync();
        await SignIn.SignInAsync(scenario.Get<string>("email"), IdentityAccessFixtures.Password);
        await Context.GotoAsync();
        await Context.AssertVisibleAsync();
    }

    [Given("a confirmed identity with no membership")]
    public async Task GivenAConfirmedIdentityWithNoMembership() => Identity = await IdentityAccessFixtures.ConfirmedIdentityAsync();

    [Given("a confirmed identity with one active membership")]
    public async Task GivenAConfirmedIdentityWithOneMembership()
    {
        Identity = await IdentityAccessFixtures.ConfirmedIdentityAsync();
        Organization = await IdentityAccessFixtures.OrganizationAsync("Acme", "members.read");
        await IdentityAccessFixtures.MembershipAsync(Organization, Identity);
    }

    [Given("a confirmed identity with two active memberships")]
    public async Task GivenAConfirmedIdentityWithTwoMemberships()
    {
        Identity = await IdentityAccessFixtures.ConfirmedIdentityAsync();
        Organization = await IdentityAccessFixtures.OrganizationAsync("Acme", "members.read");
        var second = await IdentityAccessFixtures.OrganizationAsync("Globex", "members.read");
        scenario.Set(second, "second");
        await IdentityAccessFixtures.MembershipAsync(Organization, Identity);
        await IdentityAccessFixtures.MembershipAsync(second, Identity);
    }

    [Given("a confirmed identity that may invite in one organization only")]
    public async Task GivenAnIdentityThatMayInviteInOneOrganization()
    {
        Identity = await IdentityAccessFixtures.ConfirmedIdentityAsync();
        Organization = await IdentityAccessFixtures.OrganizationAsync("Acme", "members.read", "members.invite");
        var second = await IdentityAccessFixtures.OrganizationAsync("Globex", "members.read");
        scenario.Set(second, "second");
        await IdentityAccessFixtures.MembershipAsync(Organization, Identity);
        await IdentityAccessFixtures.MembershipAsync(second, Identity);
    }

    [When("they sign in")]
    public async Task WhenTheySignIn()
    {
        await SignIn.GotoAsync();
        await SignIn.SignInAsync(Identity.Email, IdentityAccessFixtures.Password);
        await Context.AssertVisibleAsync();
    }

    /// <summary>
    /// Registering while signed in is a distinct path: the handler reuses the identity in the session instead
    /// of creating one, and the form does not ask the caller to resubmit credentials the session already proves.
    /// </summary>
    [When("they register another organization with their own address")]
    public async Task WhenTheyRegisterAnotherOrganization()
    {
        var cuit = IdentityAccessFixtures.NextCuit();
        scenario.Set($"org-{cuit}", "secondSlug");
        await Register.GotoAsync();
        await Register.RegisterSignedInAsync($"Acceptance {Guid.NewGuid():N}", cuit);
        await Register.AssertNeutralAcknowledgementAsync();
    }

    [When("the new organization is confirmed")]
    public async Task WhenTheNewOrganizationIsConfirmed() => await ConfirmThroughDeliveredMailAsync(Identity.Email);

    [Then("both organizations are offered to them")]
    public async Task ThenBothOrganizationsAreOffered()
    {
        await Tenants.GotoAsync();
        await Tenants.AssertOffersAsync(Organization.Slug);
        await Tenants.AssertOffersAsync(scenario.Get<string>("secondSlug"));
    }

    [When("they select the second organization")]
    public async Task WhenTheySelectTheSecond()
    {
        await Tenants.GotoAsync();
        await Tenants.ChooseAsync(scenario.Get<IdentityAccessFixtures.SeededOrganization>("second").Slug);
    }

    [When("they select the organization where they may not invite")]
    public Task WhenTheySelectTheOrganizationWithoutInvite() => WhenTheySelectTheSecond();

    [Then("their access page shows no active organization")]
    public async Task ThenNoActiveOrganization()
    {
        await Context.GotoAsync();
        await Context.AssertActiveOrganizationAsync("none selected");
    }

    [Then("their access page shows that organization as active")]
    public async Task ThenThatOrganizationIsActive()
    {
        await Context.GotoAsync();
        await Context.AssertActiveOrganizationAsync(Organization.Slug);
    }

    [Then("their access page shows the second organization as active")]
    public async Task ThenTheSecondOrganizationIsActive()
    {
        await Context.GotoAsync();
        await Context.AssertActiveOrganizationAsync(scenario.Get<IdentityAccessFixtures.SeededOrganization>("second").Slug);
    }

    [Then("the invite action is not offered")]
    public async Task ThenTheInviteActionIsNotOffered()
    {
        await Context.GotoAsync();
        await Assertions.Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Invite a member" })).ToHaveCountAsync(0);
    }

    [Given("a member invites a newcomer")]
    public async Task GivenAMemberInvitesANewcomer()
    {
        Organization = await IdentityAccessFixtures.OrganizationAsync("Acme", "members.read", "members.invite");
        var recipient = $"newcomer-{Guid.NewGuid():N}@example.test";
        scenario.Set(recipient, "email");
        scenario.Set(await IdentityAccessFixtures.InvitationAsync(Organization, recipient), "token");
    }

    [Given("a confirmed identity is invited to an organization")]
    public async Task GivenAConfirmedIdentityIsInvited()
    {
        Identity = await IdentityAccessFixtures.ConfirmedIdentityAsync();
        Organization = await IdentityAccessFixtures.OrganizationAsync("Acme", "members.read", "members.invite");
        scenario.Set(await IdentityAccessFixtures.InvitationAsync(Organization, Identity.Email), "token");
    }

    [When("the newcomer registers from the invitation")]
    public async Task WhenTheNewcomerRegisters()
    {
        await Invitations.GotoRegisterAsync(scenario.Get<string>("token"));
        await Invitations.AssertFragmentClearedAsync();
        await Invitations.RegisterAsync(IdentityAccessFixtures.Password);
        await Invitations.AssertNeutralAcknowledgementAsync();
    }

    [When("the newcomer confirms the address")]
    public async Task WhenTheNewcomerConfirms()
    {
        Identity = await ConfirmThroughDeliveredMailAsync(scenario.Get<string>("email"));
    }

    [When("the newcomer signs in and accepts the invitation")]
    public async Task WhenTheNewcomerAccepts()
    {
        await SignIn.GotoAsync();
        await SignIn.SignInAsync(Identity.Email, IdentityAccessFixtures.Password);
        await AcceptOnceAsync();
    }

    [When("they sign in and accept the invitation")]
    public async Task WhenTheySignInAndAccept()
    {
        await SignIn.GotoAsync();
        await SignIn.SignInAsync(Identity.Email, IdentityAccessFixtures.Password);
        await AcceptOnceAsync();
    }

    [When("the newcomer accepts the same invitation again")]
    public Task WhenTheNewcomerAcceptsAgain() => AcceptOnceAsync();

    [Then("they hold one membership in the inviting organization")]
    public async Task ThenTheyHoldOneMembership() =>
        (await IdentityAccessFixtures.MembershipCountAsync(Organization, Identity.Id)).ShouldBe(1);

    [Then("the accepted organization can be selected through application navigation")]
    public async Task ThenAcceptedOrganizationIsAvailableWithoutReload()
    {
        await Page.GetByRole(AriaRole.Link, new() { Name = "Organizations", Exact = true }).ClickAsync();
        await Tenants.AssertOffersAsync(Organization.Slug);
        await Tenants.ChooseAsync(Organization.Slug);
        await Page.GetByRole(AriaRole.Link, new() { Name = "Your access", Exact = true }).ClickAsync();
        await Context.AssertActiveOrganizationAsync(Organization.Slug);
    }

    [Then("they still hold one membership")]
    public Task ThenTheyStillHoldOneMembership() => ThenTheyHoldOneMembership();

    [When("their session is revoked")]
    public Task WhenTheirSessionIsRevoked() => IdentityAccessFixtures.RevokeSessionsAsync(Identity.Id);

    [Then("the next protected page sends them back to sign in")]
    public async Task ThenTheNextProtectedPageRedirects()
    {
        await Context.GotoAsync();
        await SignIn.AssertVisibleAsync();
    }

    [When("the account is locked out by repeated failures")]
    public Task WhenTheAccountIsLockedOut() => IdentityAccessFixtures.LockOutAsync(Identity.Id);

    [Then("a correct password is still refused")]
    public async Task ThenACorrectPasswordIsRefused()
    {
        await SignIn.GotoAsync();
        await SignIn.AttemptSignInAsync(Identity.Email, IdentityAccessFixtures.Password);
        await SignIn.AssertProblemAsync();
    }

    /// <summary>A throttle that punished the wrong account would make one attacker able to lock out everyone.</summary>
    [Then("an unrelated account can still sign in")]
    public async Task ThenAnUnrelatedAccountCanSignIn()
    {
        var other = await IdentityAccessFixtures.ConfirmedIdentityAsync();
        await SignIn.GotoAsync();
        await SignIn.SignInAsync(other.Email, IdentityAccessFixtures.Password);
        await Context.GotoAsync();
        await Context.AssertVisibleAsync();
    }

    /// <summary>A lockout that never lifted would be a denial of service an attacker could aim at anyone.</summary>
    [Then("the account signs in again once the lockout has passed")]
    public async Task ThenTheAccountSignsInAgain()
    {
        await SignIn.SignOutAsync(); // The unrelated account is still signed in on this browser.
        await IdentityAccessFixtures.ExpireLockOutAsync(Identity.Id);
        await SignIn.GotoAsync();
        await SignIn.SignInAsync(Identity.Email, IdentityAccessFixtures.Password);
        await Context.GotoAsync();
        await Context.AssertVisibleAsync();
    }

    [Then("every identity route the client calls is declared in the served OpenAPI document")]
    public async Task ThenEveryClientRouteIsDeclared()
    {
        var paths = await ServedOpenApiPathsAsync();
        foreach (var route in new[]
                 {
                     "/api/identity/antiforgery", "/api/identity/context", "/api/identity/context/tenant",
                     "/api/identity/sessions", "/api/identity/sessions/current", "/api/identity/organizations/register",
                     "/api/identity/confirm-email", "/api/tenants/{tenantId}/invitations",
                     "/api/invitations/register", "/api/invitations/accept",
                 })
        {
            paths.ShouldContain(route, $"the client calls {route} and the served document must declare it");
        }
    }

    [Then("no legacy identity route is served")]
    public async Task ThenNoLegacyRouteIsServed()
    {
        var paths = await ServedOpenApiPathsAsync();
        paths.ShouldNotContain(path => path.Contains("/api/identity/invitations", StringComparison.Ordinal));
        paths.ShouldNotContain(path => path.Contains("preview", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<string[]> ServedOpenApiPathsAsync()
    {
        using var client = new HttpClient { BaseAddress = new Uri(AspireSetup.App.GetEndpoint(Services.WebApi).ToString()) };
        using var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
        return document.RootElement.GetProperty("paths").EnumerateObject().Select(path => path.Name).ToArray();
    }

    private async Task AcceptOnceAsync()
    {
        await Invitations.GotoAcceptAsync(scenario.Get<string>("token"));
        await Invitations.AssertFragmentClearedAsync();
        await Invitations.AcceptAsync();
        await Invitations.AssertAcceptedAsync();
    }

    /// <summary>
    /// Confirms the address the way its recipient does: by opening the link that was delivered to them and
    /// pressing the button on the screen it opens.
    /// <para>
    /// This used to be two UPDATE statements. They made every scenario downstream of confirmation pass without
    /// anything having confirmed anything — which is exactly how a delivered link that opened no screen at all
    /// survived (R1). One helper serves all three call sites because both confirmation mails, the organization's
    /// and the invited member's, carry the same subject to the same page.
    /// </para>
    /// </summary>
    private async Task<IdentityAccessFixtures.SeededIdentity> ConfirmThroughDeliveredMailAsync(string email)
    {
        var delivered = await PlatformFixtures.DeliveredAsync(email, "Confirm your email");
        await Confirmation.OpenDeliveredAsync(delivered);
        await Confirmation.AssertFragmentClearedAsync();
        await Confirmation.ConfirmAsync();
        return new IdentityAccessFixtures.SeededIdentity(await IdentityAccessFixtures.IdentityIdAsync(email), email);
    }
}

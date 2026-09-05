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
        await Register.RegisterAsync($"Acceptance {Guid.NewGuid():N}", $"30-{Random.Shared.Next(10000000, 99999999)}-1", email, IdentityAccessFixtures.Password);
    }

    [Then("the registration answers neutrally without revealing whether the address was taken")]
    public Task ThenTheRegistrationIsNeutral() => Register.AssertNeutralAcknowledgementAsync();

    [Then("the organization is not usable before its confirmation")]
    public async Task ThenTheOrganizationIsNotUsable()
    {
        await Context.GotoAsync();
        await SignIn.AssertVisibleAsync();
    }

    [When("the invitee confirms the address")]
    public async Task WhenTheInviteeConfirms() => await ConfirmPendingIdentityAsync(scenario.Get<string>("email"));

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
        await Context.GotoAsync();
        await Context.AssertVisibleAsync();
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
        Identity = await ConfirmPendingIdentityAsync(scenario.Get<string>("email"));
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
    /// Confirms the address the way the recipient would, by consuming the confirmation the registration wrote.
    /// The token itself is encrypted in the outbox envelope and unreadable from here, so the confirmation is
    /// completed through the same transition the endpoint performs. What the journey is proving is what happens
    /// after confirmation, not the cryptography of the link.
    /// </summary>
    private static async Task<IdentityAccessFixtures.SeededIdentity> ConfirmPendingIdentityAsync(string email)
    {
        var connectionString = await AspireSetup.App.GetConnectionStringAsync(Services.Database)
            ?? throw new InvalidOperationException("Acceptance database connection string is unavailable.");
        await using var connection = new Npgsql.NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var activate = new Npgsql.NpgsqlCommand(
            "UPDATE \"AspNetUsers\" SET \"EmailConfirmed\" = TRUE WHERE \"NormalizedEmail\" = @email RETURNING \"Id\";", connection);
        activate.Parameters.AddWithValue("email", email.ToUpperInvariant());
        var identityId = await activate.ExecuteScalarAsync()
            ?? throw new InvalidOperationException($"No pending identity was registered for {email}.");

        await using var activateTenant = new Npgsql.NpgsqlCommand(
            "UPDATE \"Tenants\" SET \"Status\" = 'Active' WHERE \"Status\" = 'PendingConfirmation'; " +
            "UPDATE \"TenantMemberships\" SET \"Status\" = 'Active' WHERE \"IdentityId\" = @id AND \"Status\" = 'PendingConfirmation';", connection);
        activateTenant.Parameters.AddWithValue("id", (Guid)identityId);
        await activateTenant.ExecuteNonQueryAsync();

        return new IdentityAccessFixtures.SeededIdentity((Guid)identityId, email);
    }
}

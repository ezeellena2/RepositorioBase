namespace CleanArchitecture.Web.AcceptanceTests.StepDefinitions;

/// <summary>
/// The journeys Tasks 19-25 added, driven through the browser.
/// <para>
/// Two of them need more than one browser: a device list is only meaningful when there is another device, and
/// what "ending" one does is only observable from that other device. Each context is a separate browser as far
/// as cookies are concerned, which is what makes them two devices rather than two tabs.
/// </para>
/// </summary>
[Binding]
public sealed class IdentityContinuationStepDefinitions(ScenarioContext scenario)
{
    private static IBrowserContext? primaryContext;
    private static IBrowserContext? otherContext;
    private static IPage? primaryPage;
    private static IPage? otherPage;

    private static IPage Page => primaryPage ?? throw new InvalidOperationException("The continuation feature has no page.");

    private static IPage Other => otherPage ?? throw new InvalidOperationException("This scenario has no second device.");

    private IdentitySignInPage SignIn => new(Page);
    private ConfirmEmailPage Confirmation => new(Page);
    private PersonalRegisterPage PersonalRegister => new(Page);
    private PersonalProfilePage Profile => new(Page);
    private OwnDevicesPage Devices => new(Page);
    private PasswordPages Passwords => new(Page);
    private OrganizationRolesPage Roles => new(Page);
    private OrganizationMembersPage Members => new(Page);
    private StandingInvitationsPage Invitations => new(Page);
    private TenantSelectorPage Tenants => new(Page);
    private AccountLifecyclePage Account => new(Page);

    private string Email
    {
        get => scenario.Get<string>("email");
        set => scenario.Set(value, "email");
    }

    private string Password
    {
        get => scenario.Get<string>("password");
        set => scenario.Set(value, "password");
    }

    [BeforeScenario("IdentityContinuation")]
    public static async Task BeforeContinuationScenario()
    {
        primaryContext = await PlaywrightSetup.NewContextAsync();
        primaryPage = await primaryContext.NewPageAsync();
    }

    [AfterScenario("IdentityContinuation")]
    public static async Task AfterContinuationScenario()
    {
        foreach (var context in new[] { primaryContext, otherContext })
        {
            if (context is not null) await context.DisposeAsync();
        }

        primaryContext = otherContext = null;
        primaryPage = otherPage = null;
    }

    // ---- a personal account of one's own ----------------------------------------------------------------

    [Given("a confirmed identity with no administrative responsibility")]
    public async Task GivenAnAccountWithoutAdministrativeResponsibility()
    {
        var identity = await IdentityAccessFixtures.ConfirmedIdentityAsync();
        Email = identity.Email;
        Password = IdentityAccessFixtures.Password;
    }

    [When("they deactivate their account from the account navigation")]
    public Task WhenTheyDeactivate() => Account.DeactivateFromNavigationAsync(Password);

    [Then("signing in is refused while their account is deactivated")]
    public async Task ThenParkedSignInIsRefused()
    {
        await Page.GetByRole(AriaRole.Link, new() { Name = "Sign in", Exact = true }).ClickAsync();
        var refused = await SignIn.AttemptSignInAsync(Email, Password);
        // Sign-in deliberately answers neutrally; no cookie and no authenticated UI is the refusal.
        (await refused.AllHeadersAsync()).ContainsKey("set-cookie").ShouldBeFalse();
        await SignIn.AssertProblemAsync();
        await SignIn.AssertVisibleAsync();
    }

    [When("they request reactivation from login and follow the delivered link")]
    public async Task WhenTheyFollowReactivationMail()
    {
        await Account.RequestFromLoginAsync(Email);
        // Both messages must pass through local delivery, not a test-only token reader.
        await PlatformFixtures.DeliveredAsync(Email, "Your account was deactivated");
        var delivered = await PlatformFixtures.DeliveredAsync(Email, "Reactivate your account");
        await Account.OpenDeliveredAsync(delivered);
    }

    [When("they reactivate with their current password")]
    public Task WhenTheyReactivate() => Account.ReactivateAsync(Password);

    [Then("reactivation has not signed them in")]
    public Task ThenReactivationCreatesNoSession() => Account.AssertSignedOutAsync();

    [Given("a visitor sets up a personal account")]
    public async Task GivenAVisitorSetsUpAPersonalAccount()
    {
        Email = $"personal-{Guid.NewGuid():N}@example.test";
        Password = IdentityAccessFixtures.Password;
        // Eight digits, drawn rather than counted: the acceptance database outlives no run now, but a document
        // is unique across every identity and a fixed one would collide with itself on a rerun.
        scenario.Set(Random.Shared.Next(10_000_000, 99_999_999).ToString(), "document");
        await PersonalRegister.GotoAsync();
        await PersonalRegister.RegisterAsync("Ana Pérez", "Ana", scenario.Get<string>("document"), Email, Password);
    }

    [Then("setting up answers neutrally without revealing whether the address was taken")]
    public Task ThenSettingUpIsNeutral() => PersonalRegister.AssertNeutralAcknowledgementAsync();

    [When("they open the delivered confirmation link and confirm")]
    public async Task WhenTheyConfirm()
    {
        var delivered = await PlatformFixtures.DeliveredAsync(Email, "Confirm your email");
        await Confirmation.OpenDeliveredAsync(delivered);
        await Confirmation.AssertFragmentClearedAsync();
        await Confirmation.ConfirmAsync();
    }

    [When("they sign in with the password they hold")]
    public async Task WhenTheySignIn()
    {
        await SignIn.GotoAsync();
        await SignIn.SignInAsync(Email, Password);
    }

    [Then("their profile shows the document masked and never the number they submitted")]
    public async Task ThenTheDocumentIsMasked()
    {
        await Profile.GotoAsync();
        await Profile.AssertDocumentMaskedAsync(scenario.Get<string>("document"));
    }

    [Given("a confirmed identity that belongs to one organization")]
    public async Task GivenAConfirmedIdentityWithOneMembership()
    {
        var identity = await IdentityAccessFixtures.ConfirmedIdentityAsync();
        Email = identity.Email;
        Password = IdentityAccessFixtures.Password;
        var organization = await IdentityAccessFixtures.OrganizationAsync("Acme", "members.read");
        await IdentityAccessFixtures.MembershipAsync(organization, identity);
        scenario.Set(organization, "organization");
    }

    [When("they add a personal context to the identity they already have")]
    public async Task WhenTheyAddAPersonalContext()
    {
        scenario.Set(Random.Shared.Next(10_000_000, 99_999_999).ToString(), "document");
        await Profile.GotoAsync();
        await Profile.AddPersonalContextAsync("Ana Pérez", "Ana", scenario.Get<string>("document"));
    }

    [Then("both the organization and their personal context are offered to them")]
    public async Task ThenBothContextsAreOffered()
    {
        var organization = scenario.Get<IdentityAccessFixtures.SeededOrganization>("organization");
        await Page.GetByRole(AriaRole.Link, new() { Name = "Organizations", Exact = true }).ClickAsync();
        await Tenants.AssertOffersAsync(organization.Slug);

        // Two contexts and one identity: the person did not acquire a second account, they acquired a second
        // place to be. The personal one is named by the tenant it is, so it is found by not being the other.
        var personal = await Tenants.OtherThanAsync(organization.Slug);
        await Tenants.ChooseAsync(personal);
        await Page.GetByRole(AriaRole.Link, new() { Name = "Your access", Exact = true }).ClickAsync();
        await new IdentityContextPage(Page).AssertActiveOrganizationAsync(personal);
    }

    // ---- devices ----------------------------------------------------------------------------------------

    [Given("a confirmed identity signed in on two devices")]
    public async Task GivenAnIdentitySignedInOnTwoDevices()
    {
        var identity = await IdentityAccessFixtures.ConfirmedIdentityAsync();
        Email = identity.Email;
        Password = IdentityAccessFixtures.Password;

        await SignIn.GotoAsync();
        await SignIn.SignInAsync(Email, Password);

        otherContext = await PlaywrightSetup.NewContextAsync();
        otherPage = await otherContext.NewPageAsync();
        var elsewhere = new IdentitySignInPage(Other);
        await elsewhere.GotoAsync();
        await elsewhere.SignInAsync(Email, Password);
    }

    [When("they end the other device from their device list")]
    public async Task WhenTheyEndTheOtherDevice()
    {
        await Devices.GotoAsync();
        await Devices.AssertVisibleAsync();
        await Devices.ProveAsync(Password);
        await Devices.EndTheOtherDeviceAsync();
    }

    [Then("the other device is sent back to sign in")]
    public async Task ThenTheOtherDeviceIsSignedOut()
    {
        await new IdentityContextPage(Other).GotoAsync();
        await new IdentitySignInPage(Other).AssertVisibleAsync();
    }

    [Then("their own device is still signed in")]
    public async Task ThenTheirOwnDeviceStillWorks()
    {
        await new IdentityContextPage(Page).GotoAsync();
        await new IdentityContextPage(Page).AssertVisibleAsync();
    }

    // ---- what a role decides, and who the organization belongs to ---------------------------------------

    [Given("an organization with an administrator and a member who holds nothing")]
    public async Task GivenAnOrganizationWithAnAdministratorAndAMember()
    {
        var administrator = await IdentityAccessFixtures.ConfirmedIdentityAsync();
        var member = await IdentityAccessFixtures.ConfirmedIdentityAsync();
        var organization = await IdentityAccessFixtures.AdministeredOrganizationAsync(administrator);
        await IdentityAccessFixtures.PlainMembershipAsync(organization, member);

        Email = administrator.Email;
        Password = IdentityAccessFixtures.Password;
        scenario.Set(administrator, "administrator");
        scenario.Set(member, "member");
        scenario.Set(organization, "organization");
        scenario.Set($"inviters-{Guid.NewGuid():N}", "role");
    }

    [When("the administrator signs in and puts the invitation permission into a role of their own")]
    public async Task WhenTheAdministratorCreatesARole()
    {
        await SignIn.GotoAsync();
        await SignIn.SignInAsync(Email, Password);
        await Roles.GotoAsync();
        await Roles.CreateWithAsync(scenario.Get<string>("role"), "members.invite", Password);
    }

    [When("they give that role to the member")]
    public async Task WhenTheyGiveTheRoleToTheMember()
    {
        await Members.GotoAsync();
        await Members.GiveRoleAsync(scenario.Get<IdentityAccessFixtures.SeededIdentity>("member").Email, scenario.Get<string>("role"), Password);
    }

    [Then("the member is offered the invitation action")]
    public Task ThenTheMemberMayInvite() => AssertMemberIsOfferedInvitingAsync(offered: true);

    [When("the administrator takes the permission back out of the role")]
    public async Task WhenTheyTakeThePermissionBack()
    {
        await Roles.GotoAsync();
        await Roles.TakePermissionOutAsync(scenario.Get<string>("role"), "members.invite", Password);
    }

    [Then("the member is no longer offered it")]
    public Task ThenTheMemberMayNotInvite() => AssertMemberIsOfferedInvitingAsync(offered: false);

    [When("the administrator signs in and hands the organization to the member")]
    public async Task WhenTheyTransferOwnership()
    {
        await SignIn.GotoAsync();
        await SignIn.SignInAsync(Email, Password);
        await Members.GotoAsync();
        await Members.TransferOwnershipAsync(scenario.Get<IdentityAccessFixtures.SeededIdentity>("member").Email, Password);
    }

    [Then("the member holds the ownership and the administrator does not")]
    public async Task ThenOwnershipMoved()
    {
        var owner = await IdentityAccessFixtures.OwnerIdentityIdAsync(scenario.Get<IdentityAccessFixtures.SeededOrganization>("organization"));
        owner.ShouldBe(scenario.Get<IdentityAccessFixtures.SeededIdentity>("member").Id);
        owner.ShouldNotBe(scenario.Get<IdentityAccessFixtures.SeededIdentity>("administrator").Id);
    }

    /// <summary>
    /// Asked of the member's own browser, because what a role grants is only meaningful as what the person it
    /// was given to can then do. A fresh browser each time, so the answer is the server's rather than a screen
    /// that has been open since before the change.
    /// </summary>
    private async Task AssertMemberIsOfferedInvitingAsync(bool offered)
    {
        var member = scenario.Get<IdentityAccessFixtures.SeededIdentity>("member");
        await using var context = await PlaywrightSetup.NewContextAsync();
        var page = await context.NewPageAsync();
        var signIn = new IdentitySignInPage(page);
        await signIn.GotoAsync();
        await signIn.SignInAsync(member.Email, IdentityAccessFixtures.Password);
        await new IdentityContextPage(page).GotoAsync();

        var invite = page.GetByRole(AriaRole.Link, new() { Name = "Invite a member" });
        await Assertions.Expect(invite).ToHaveCountAsync(offered ? 1 : 0);
    }

    // ---- one usable offer, and then none ----------------------------------------------------------------

    [Given("an identity that has been invited to it")]
    public async Task GivenAnIdentityHasBeenInvited()
    {
        var invitee = await IdentityAccessFixtures.ConfirmedIdentityAsync();
        scenario.Set(invitee, "invitee");

        await SignIn.GotoAsync();
        await SignIn.SignInAsync(Email, Password);
        await Invitations.GotoAsync();
        await Invitations.InviteAsync(invitee.Email, RoleNameOf(scenario.Get<IdentityAccessFixtures.SeededOrganization>("organization")));

        var first = await PlatformFixtures.DeliveredAsync(invitee.Email, "You have been invited");
        scenario.Set(first, "first");
    }

    [When("the administrator resends the invitation")]
    public async Task WhenTheAdministratorResends()
    {
        var invitee = scenario.Get<IdentityAccessFixtures.SeededIdentity>("invitee");
        await Invitations.GotoAsync();
        await Invitations.ResendAsync(invitee.Email);

        // Named by the file the first mail arrived in, so "the newer one" is a fact rather than a guess about
        // which of two files a clock wrote first.
        var first = scenario.Get<PlatformFixtures.DeliveredMessage>("first");
        scenario.Set(await PlatformFixtures.DeliveredAsync(invitee.Email, "You have been invited", [first.DropFile]), "second");
    }

    [Then("the link that was replaced no longer accepts")]
    public Task ThenTheReplacedLinkIsDead() => AssertLinkRefusedAsync(scenario.Get<PlatformFixtures.DeliveredMessage>("first"));

    [When("the administrator withdraws the invitation")]
    public async Task WhenTheAdministratorWithdraws()
    {
        await Invitations.GotoAsync();
        await Invitations.WithdrawAsync(scenario.Get<IdentityAccessFixtures.SeededIdentity>("invitee").Email);
    }

    [Then("the link that replaced it no longer accepts either")]
    public Task ThenTheReplacementIsDeadToo() => AssertLinkRefusedAsync(scenario.Get<PlatformFixtures.DeliveredMessage>("second"));

    [Then("the invitee holds no membership")]
    public async Task ThenTheInviteeHoldsNothing()
    {
        var invitee = scenario.Get<IdentityAccessFixtures.SeededIdentity>("invitee");
        (await IdentityAccessFixtures.MembershipCountAsync(scenario.Get<IdentityAccessFixtures.SeededOrganization>("organization"), invitee.Id))
            .ShouldBe(0, "an offer that was rotated and then withdrawn made nobody a member");
    }

    /// <summary>
    /// The invitee opens the link out of their own mailbox, in their own browser, signed in as themselves — and
    /// is refused. Asserting from the administrator's browser would be asking the wrong person.
    /// </summary>
    private async Task AssertLinkRefusedAsync(PlatformFixtures.DeliveredMessage delivered)
    {
        var invitee = scenario.Get<IdentityAccessFixtures.SeededIdentity>("invitee");
        await using var context = await PlaywrightSetup.NewContextAsync();
        var page = await context.NewPageAsync();
        var signIn = new IdentitySignInPage(page);
        await signIn.GotoAsync();
        await signIn.SignInAsync(invitee.Email, IdentityAccessFixtures.Password);

        await page.GotoAsync($"{delivered.Path}{delivered.Fragment}".StartsWith('/')
            ? new Uri(new Uri(page.Url), $"{delivered.Path}{delivered.Fragment}").ToString()
            : $"{delivered.Path}{delivered.Fragment}");
        await page.ReloadAsync();
        await new InvitationPages(page).AcceptAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Alert)).ToBeVisibleAsync();
    }

    /// <summary>The role the fixture created, which is what the invite screen offers by name.</summary>
    private static string RoleNameOf(IdentityAccessFixtures.SeededOrganization organization) => $"role-{organization.RoleId:N}";

    // ---- the two ways a password moves ------------------------------------------------------------------

    [Given("a confirmed identity that cannot remember its password")]
    public async Task GivenAnIdentityThatForgotItsPassword()
    {
        var identity = await IdentityAccessFixtures.ConfirmedIdentityAsync();
        Email = identity.Email;
        Password = IdentityAccessFixtures.Password;
    }

    [When("they ask for a reset link and follow the one delivered")]
    public async Task WhenTheyResetFromTheDeliveredLink()
    {
        await Passwords.GotoAsync();
        await Passwords.AskForALinkAsync(Email);

        var delivered = await PlatformFixtures.DeliveredAsync(Email, "Reset your password");
        await Passwords.OpenDeliveredAsync(delivered);
        await Passwords.AssertFragmentClearedAsync();
        await Passwords.ChooseAsync(ChosenPassword);
    }

    [Then("the password they had no longer signs them in")]
    public Task ThenTheOldPasswordIsRefused() => AssertRefusedAsync(Password);

    [Then("the one they chose does")]
    public Task ThenTheChosenPasswordWorks() => AssertAcceptedAsync(ChosenPassword);

    [When("they change their password from inside")]
    public async Task WhenTheyChangeItFromInside()
    {
        // Signed in on this browser first: "from inside" is the point of the step, and the checks above ran in
        // browsers of their own precisely so they would leave this one holding nothing.
        await SignIn.GotoAsync();
        await SignIn.SignInAsync(Email, ChosenPassword);
        await Passwords.ChangeAsync(ChosenPassword, ReplacementPassword);
    }

    [Then("the password they just replaced no longer signs them in")]
    public async Task ThenTheReplacedPasswordIsRefused()
    {
        await AssertRefusedAsync(ChosenPassword);
        await AssertAcceptedAsync(ReplacementPassword);
    }

    private const string ChosenPassword = "Chosen4Acceptance!";
    private const string ReplacementPassword = "Replaced4Acceptance!";

    /// <summary>
    /// A refused sign-in is deliberately the same bodyless answer a wrong address gets, so what is asserted is
    /// that no session came of it.
    /// <para>
    /// Both password checks run in a browser of their own. Whether a password still works is a fact about the
    /// credential, not about the browser that changed it — and asking the browser that just changed one means
    /// asking a page whose session was rotated underneath it, which answers about the wrong thing.
    /// </para>
    /// </summary>
    private Task AssertRefusedAsync(string password) => InAFreshBrowserAsync(async signIn =>
    {
        var response = await signIn.AttemptSignInAsync(Email, password);
        response.Status.ShouldBe(204, "a refused sign-in is neutral, not an error");
        response.Headers.ContainsKey("set-cookie").ShouldBeFalse("a refused sign-in issues no session");
        await signIn.AssertVisibleAsync();
    });

    private Task AssertAcceptedAsync(string password) =>
        InAFreshBrowserAsync(signIn => signIn.SignInAsync(Email, password));

    private static async Task InAFreshBrowserAsync(Func<IdentitySignInPage, Task> ask)
    {
        await using var context = await PlaywrightSetup.NewContextAsync();
        var page = await context.NewPageAsync();
        var signIn = new IdentitySignInPage(page);
        await signIn.GotoAsync();
        await ask(signIn);
    }
}

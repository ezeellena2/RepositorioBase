using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.Common.Localization;
using CleanArchitecture.Domain.IdentityAccess.Authorization;
using CleanArchitecture.Domain.IdentityAccess.Credentials;
using CleanArchitecture.Domain.IdentityAccess.Identities;
using CleanArchitecture.Domain.IdentityAccess.Invitations;
using CleanArchitecture.Domain.IdentityAccess.Organizations;
using CleanArchitecture.Domain.IdentityAccess.Outbox;
using CleanArchitecture.Domain.IdentityAccess.People;
using CleanArchitecture.Domain.IdentityAccess.Platform;
using CleanArchitecture.Domain.IdentityAccess.Security;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using CleanArchitecture.Infrastructure.Data;
using CleanArchitecture.Infrastructure.Email;
using CleanArchitecture.Infrastructure.Identity;
using CleanArchitecture.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CleanArchitecture.Infrastructure.IntegrationTests.IdentityAccess;

/// <summary>
/// The executable Phase 4 delivery catalog. It resolves the production registrations, reads every recipient from
/// PostgreSQL, and renders each logical branch in every supported language rather than merely comparing resource
/// names (PLAN section 11.6).
/// </summary>
[NonParallelizable]
public sealed class IdentityEmailDeliveryMatrixTests
{
    private const string Origin = "https://app.example.test";
    private const string Token = "phase-four-matrix-token";

    [Test]
    public async Task Every_registered_identity_email_variant_renders_in_every_supported_language()
    {
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<IdentityEmailOptions>>().Value;
        var previousOrigin = options.PublicOrigin;
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            options.PublicOrigin = Origin;
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ja-JP");
            await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var transaction = await context.Database.BeginTransactionAsync();
                try
                {
                    var seeded = await SeedAsync(context, scope.ServiceProvider.GetRequiredService<TimeProvider>());
                    var handlers = scope.ServiceProvider.GetServices<IOutboxDeliveryHandler>().ToArray();
                    var expectedTypes = new[]
                    {
                        "identity.invitation.requested",
                        "identity.confirmation.requested",
                        "identity.invitation.confirmation.requested",
                        "identity.invitation.signin.notice.requested",
                        "identity.registration.confirmation.requested",
                        "identity.registration.signin.notice.requested",
                        "identity.personal.confirmation.requested",
                        "identity.personal.signin.notice.requested",
                        "identity.password.recovery.requested",
                        "identity.reactivation.requested",
                        "identity.lifecycle.self.deactivated.notice.requested",
                        "identity.lifecycle.administratively.suspended.notice.requested",
                        "identity.lifecycle.reactivated.notice.requested",
                        "platform.invitation.requested",
                        "platform.invitation.confirmation.requested",
                        "platform.invitation.signin.notice.requested"
                    };
                    handlers.Length.ShouldBe(expectedTypes.Length);
                    handlers.Select(handler => handler.MessageType).Distinct().Count().ShouldBe(handlers.Length);
                    handlers.Select(handler => handler.MessageType).Order().ShouldBe(expectedTypes.Order());

                    var cases = Cases(seeded);
                    cases.Count.ShouldBe(17);
                    foreach (var language in LocalizationRegistry.SupportedLanguages)
                    {
                        foreach (var scenario in cases)
                        {
                            var handler = handlers.Single(candidate => candidate.MessageType == scenario.MessageType);
                            handler.RequiresSecret.ShouldBe(scenario.RequiresSecret, scenario.Name);

                            var email = await handler.PrepareAsync(
                                scenario.Payload,
                                scenario.RequiresSecret ? Token : null,
                                language,
                                CancellationToken.None);

                            email.ShouldNotBeNull(scenario.Name);
                            email.Language.ShouldBe(language, scenario.Name);
                            email.Recipient.ShouldBe(scenario.Recipient, scenario.Name);
                            email.Subject.ShouldBe(language == "es" ? scenario.SpanishSubject : scenario.EnglishSubject, scenario.Name);
                            Assert.That(
                                email.Body,
                                Does.StartWith(language == "es" ? scenario.SpanishLead : scenario.EnglishLead),
                                scenario.Name);
                            var exactBody = language == "es" ? scenario.SpanishBody : scenario.EnglishBody;
                            if (exactBody is not null) email.Body.ShouldBe(exactBody, scenario.Name);
                            Assert.That(email.Body, Does.Contain($"{Origin}{scenario.Path}"), scenario.Name);
                            Regex.IsMatch(email.Subject, @"\{\d+[^}]*\}").ShouldBeFalse(scenario.Name);
                            Regex.IsMatch(email.Body, @"\{\d+[^}]*\}").ShouldBeFalse(scenario.Name);
                            if (scenario.RequiresSecret)
                                Assert.That(email.Body, Does.Contain($"#token={Uri.EscapeDataString(Token)}"), scenario.Name);
                            else
                                Assert.That(email.Body, Does.Not.Contain("#token="), scenario.Name);
                        }
                    }

                    context.ChangeTracker.Clear();
                    await AssertResolutionOrderAsync(
                        context,
                        handlers,
                        seeded,
                        scope.ServiceProvider.GetRequiredService<LocalizationSettings>().DefaultLanguage);
                }
                finally
                {
                    await transaction.RollbackAsync();
                }
            });
            CultureInfo.CurrentCulture.Name.ShouldBe("fr-FR");
            CultureInfo.CurrentUICulture.Name.ShouldBe("ja-JP");
        }
        finally
        {
            options.PublicOrigin = previousOrigin;
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    private static async Task<Seeded> SeedAsync(ApplicationDbContext context, TimeProvider timeProvider)
    {
        var now = timeProvider.GetUtcNow();
        var organization = Tenant.CreateOrganization(TenantSlug.From($"email-matrix-{Guid.NewGuid():N}"));
        organization.Activate();
        var role = Role.Create(organization, $"member-{Guid.NewGuid():N}");
        var invitedEmail = $"invited-{Guid.NewGuid():N}@example.test";
        var organizationInvitation = Invitation.Issue(
            organization,
            invitedEmail,
            [role],
            VersionedTokenHash.Of($"organization-{Guid.NewGuid():N}"),
            "es",
            now,
            now.AddDays(1));

        var account = User($"account-{Guid.NewGuid():N}@example.test", IdentityAccountStatus.SelfDeactivated);
        var invitedAccount = User(invitedEmail, IdentityAccountStatus.Active);
        var platformAccount = User($"platform-account-{Guid.NewGuid():N}@example.test", IdentityAccountStatus.Active);
        var reset = PasswordResetRequest.Issue(account.Id, VersionedTokenHash.Of(Token), now, TimeSpan.FromDays(1));
        var reactivation = AccountReactivationRequest.Issue(account.Id, VersionedTokenHash.Of(Token), now, TimeSpan.FromDays(1));

        var organizationConfirmationSubmission = RegistrationSubmission.Claim($"org-confirm-{Guid.NewGuid():N}", now);
        var organizationSignInSubmission = RegistrationSubmission.Claim($"org-signin-{Guid.NewGuid():N}", now);
        var organizationConfirmation = PendingRegistrationIntent.Open(
            organizationConfirmationSubmission.Id,
            $"org-confirm-{Guid.NewGuid():N}@example.test",
            "Acme S.A.",
            NormalizedCuit.From("30-71234567-4"),
            "password-hash",
            "es",
            now,
            now.AddDays(1));
        var organizationIntentAccount = User(organizationConfirmation.NormalizedEmail, IdentityAccountStatus.Active);
        var organizationSignIn = PendingRegistrationIntent.Notify(
            organizationSignInSubmission.Id,
            $"org-signin-{Guid.NewGuid():N}@example.test",
            "Acme S.A.",
            NormalizedCuit.From("30-71234567-4"),
            "es",
            now,
            now.AddDays(1));

        var personalConfirmationSubmission = RegistrationSubmission.Claim($"personal-confirm-{Guid.NewGuid():N}", now);
        var personalSignInSubmission = RegistrationSubmission.Claim($"personal-signin-{Guid.NewGuid():N}", now);
        var personalConfirmation = PendingPersonalIntent.Open(
            personalConfirmationSubmission.Id,
            $"personal-confirm-{Guid.NewGuid():N}@example.test",
            "Ana Example",
            "Ana",
            "sealed-document",
            "password-hash",
            "es",
            now,
            now.AddDays(1));
        var personalIntentAccount = User(personalConfirmation.NormalizedEmail, IdentityAccountStatus.Active);
        var personalSignIn = PendingPersonalIntent.Notify(
            personalSignInSubmission.Id,
            $"personal-signin-{Guid.NewGuid():N}@example.test",
            "Ana Example",
            "Ana",
            "es",
            now,
            now.AddDays(1));

        var platform = await context.Tenants.SingleOrDefaultAsync(tenant => tenant.Type == TenantType.Platform);
        if (platform is null)
        {
            platform = Tenant.CreatePlatform();
            platform.Activate();
            context.Tenants.Add(platform);
        }

        var platformOwner = await context.PlatformAdminInvitations
            .FirstOrDefaultAsync(invitation => invitation.IsOwner && invitation.Status == PlatformAdminInvitationStatus.Pending);
        if (platformOwner is null)
        {
            platformOwner = PlatformAdminInvitation.Issue(
                platform,
                $"platform-owner-{Guid.NewGuid():N}@example.test",
                VersionedTokenHash.Of($"platform-owner-{Guid.NewGuid():N}"),
                true,
                "es",
                now,
                now.AddDays(1));
            context.PlatformAdminInvitations.Add(platformOwner);
        }
        var platformAdministrator = PlatformAdminInvitation.Issue(
            platform,
            platformAccount.Email!,
            VersionedTokenHash.Of($"platform-admin-{Guid.NewGuid():N}"),
            false,
            "es",
            now,
            now.AddDays(1));

        context.AddRange(
            organization,
            role,
            organizationInvitation,
            account,
            invitedAccount,
            platformAccount,
            organizationIntentAccount,
            personalIntentAccount,
            reset,
            reactivation,
            organizationConfirmationSubmission,
            organizationSignInSubmission,
            organizationConfirmation,
            organizationSignIn,
            personalConfirmationSubmission,
            personalSignInSubmission,
            personalConfirmation,
            personalSignIn,
            platformAdministrator);
        await context.SaveChangesAsync();

        return new Seeded(
            account,
            invitedAccount,
            platformAccount,
            organizationIntentAccount,
            personalIntentAccount,
            organizationInvitation,
            organizationConfirmation,
            organizationSignIn,
            personalConfirmation,
            personalSignIn,
            reset,
            reactivation,
            platformOwner,
            platformAdministrator);
    }

    private static async Task AssertResolutionOrderAsync(
        ApplicationDbContext context,
        IReadOnlyCollection<IOutboxDeliveryHandler> handlers,
        Seeded seeded,
        string defaultLanguage)
    {
        var snapshotLanguage = LocalizationRegistry.SupportedLanguages
            .Single(language => language != defaultLanguage);

        await AssertSnapshotHierarchyAsync(
            context,
            handlers.Single(handler => handler.MessageType == "identity.invitation.requested"),
            JsonSerializer.Serialize(new { InvitationId = seeded.OrganizationInvitation.Id.Value }),
            seeded.InvitedAccount.Id,
            language => context.Invitations
                .Where(invitation => invitation.Id == seeded.OrganizationInvitation.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(invitation => invitation.Language, language)),
            snapshotLanguage,
            defaultLanguage,
            "organization invitation");

        await AssertSnapshotHierarchyAsync(
            context,
            handlers.Single(handler => handler.MessageType == "identity.registration.confirmation.requested"),
            JsonSerializer.Serialize(new { IntentId = seeded.OrganizationConfirmation.Id }),
            seeded.OrganizationIntentAccount.Id,
            language => context.PendingRegistrationIntents
                .Where(intent => intent.Id == seeded.OrganizationConfirmation.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(intent => intent.Language, language)),
            snapshotLanguage,
            defaultLanguage,
            "organization registration intent");

        await AssertSnapshotHierarchyAsync(
            context,
            handlers.Single(handler => handler.MessageType == "identity.personal.confirmation.requested"),
            JsonSerializer.Serialize(new { IntentId = seeded.PersonalConfirmation.Id }),
            seeded.PersonalIntentAccount.Id,
            language => context.PendingPersonalIntents
                .Where(intent => intent.Id == seeded.PersonalConfirmation.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(intent => intent.Language, language)),
            snapshotLanguage,
            defaultLanguage,
            "personal registration intent");

        await AssertSnapshotHierarchyAsync(
            context,
            handlers.Single(handler => handler.MessageType == "platform.invitation.requested"),
            JsonSerializer.Serialize(new { InvitationId = seeded.PlatformAdministrator.Id.Value }),
            seeded.PlatformAccount.Id,
            language => context.PlatformAdminInvitations
                .Where(invitation => invitation.Id == seeded.PlatformAdministrator.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(invitation => invitation.Language, language)),
            snapshotLanguage,
            defaultLanguage,
            "Platform invitation");

        await AssertAccountOnlyHierarchyAsync(
            context,
            handlers.Single(handler => handler.MessageType == "identity.confirmation.requested"),
            JsonSerializer.Serialize(new { IdentityId = seeded.Account.Id }),
            seeded.Account.Id,
            snapshotLanguage,
            defaultLanguage);
    }

    private static async Task AssertSnapshotHierarchyAsync(
        ApplicationDbContext context,
        IOutboxDeliveryHandler handler,
        string payload,
        Guid accountId,
        Func<string?, Task<int>> setSnapshot,
        string snapshotLanguage,
        string defaultLanguage,
        string because)
    {
        await setSnapshot(snapshotLanguage);
        await SetAccountLanguageAsync(context, accountId, defaultLanguage);
        (await PrepareAsync(handler, payload)).Language.ShouldBe(defaultLanguage, $"{because}: account preference");

        await SetAccountLanguageAsync(context, accountId, null);
        (await PrepareAsync(handler, payload)).Language.ShouldBe(snapshotLanguage, $"{because}: snapshot");

        await setSnapshot(null);
        (await PrepareAsync(handler, payload)).Language.ShouldBe(defaultLanguage, $"{because}: legacy null snapshot");
    }

    private static async Task AssertAccountOnlyHierarchyAsync(
        ApplicationDbContext context,
        IOutboxDeliveryHandler handler,
        string payload,
        Guid accountId,
        string accountLanguage,
        string defaultLanguage)
    {
        await SetAccountLanguageAsync(context, accountId, accountLanguage);
        (await PrepareAsync(handler, payload)).Language.ShouldBe(accountLanguage, "account-only preference");

        await SetAccountLanguageAsync(context, accountId, null);
        (await PrepareAsync(handler, payload)).Language.ShouldBe(defaultLanguage, "account-only legacy null preference");
    }

    private static Task<int> SetAccountLanguageAsync(
        ApplicationDbContext context,
        Guid identityId,
        string? language) =>
        context.Users
            .Where(user => user.Id == identityId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.PreferredLanguage, language));

    private static async Task<IdentityEmail> PrepareAsync(IOutboxDeliveryHandler handler, string payload)
    {
        var email = await handler.PrepareAsync(
            payload,
            handler.RequiresSecret ? Token : null,
            null,
            CancellationToken.None);
        return email.ShouldNotBeNull();
    }

    private static ApplicationUser User(string email, IdentityAccountStatus status) => new()
    {
        Id = Guid.NewGuid(),
        UserName = email,
        NormalizedUserName = email.ToUpperInvariant(),
        Email = email,
        NormalizedEmail = email.ToUpperInvariant(),
        EmailConfirmed = true,
        Status = status,
        SecurityStamp = Guid.NewGuid().ToString("N")
    };

    private static IReadOnlyList<EmailCase> Cases(Seeded seeded) =>
    [
        new("organization invitation", "identity.invitation.requested",
            JsonSerializer.Serialize(new { InvitationId = seeded.OrganizationInvitation.Id.Value }),
            seeded.OrganizationInvitation.NormalizedEmail, true, "/invitations/accept",
            "You have been invited", "Tiene una invitación",
            "Open this link to accept:", "Abra este enlace para aceptar la invitación:"),
        new("ordinary confirmation", "identity.confirmation.requested",
            JsonSerializer.Serialize(new { IdentityId = seeded.Account.Id }),
            seeded.Account.Email!, true, "/confirm-email",
            "Confirm your email", "Confirme su correo electrónico",
            "Open this link to confirm:", "Abra este enlace para confirmar su correo electrónico:"),
        new("invited confirmation", "identity.invitation.confirmation.requested",
            JsonSerializer.Serialize(new { IdentityId = seeded.InvitedAccount.Id, InvitationId = seeded.OrganizationInvitation.Id.Value }),
            seeded.InvitedAccount.Email!, true, "/confirm-email",
            "Confirm your email", "Confirme su correo electrónico",
            "Open this link to confirm:", "Abra este enlace para confirmar su correo electrónico:"),
        new("invited existing-account sign-in", "identity.invitation.signin.notice.requested",
            JsonSerializer.Serialize(new { IdentityId = seeded.InvitedAccount.Id, InvitationId = seeded.OrganizationInvitation.Id.Value }),
            seeded.InvitedAccount.Email!, false, "/login",
            "Sign in to your account", "Inicie sesión en su cuenta",
            "Someone tried to register with your email", "Alguien intentó registrarse con su correo electrónico",
            $"Someone tried to register with your email. You can sign in at {Origin}/login.",
            $"Alguien intentó registrarse con su correo electrónico. Puede iniciar sesión en {Origin}/login."),
        new("organization registration confirmation", "identity.registration.confirmation.requested",
            JsonSerializer.Serialize(new { IntentId = seeded.OrganizationConfirmation.Id }),
            seeded.OrganizationConfirmation.NormalizedEmail, true, "/confirm-email",
            "Confirm your email", "Confirme su correo electrónico",
            "Open this link to finish registering your organization:", "Abra este enlace para terminar de registrar su organización:"),
        new("organization registration sign-in", "identity.registration.signin.notice.requested",
            JsonSerializer.Serialize(new { IntentId = seeded.OrganizationSignIn.Id }),
            seeded.OrganizationSignIn.NormalizedEmail, false, "/login",
            "Sign in to your account", "Inicie sesión en su cuenta",
            "Someone tried to register an organization", "Alguien intentó registrar una organización"),
        new("personal registration confirmation", "identity.personal.confirmation.requested",
            JsonSerializer.Serialize(new { IntentId = seeded.PersonalConfirmation.Id }),
            seeded.PersonalConfirmation.NormalizedEmail, true, "/confirm-email",
            "Confirm your email", "Confirme su correo electrónico",
            "Open this link to finish setting up your personal account:", "Abra este enlace para terminar de configurar su cuenta personal:"),
        new("personal registration sign-in", "identity.personal.signin.notice.requested",
            JsonSerializer.Serialize(new { IntentId = seeded.PersonalSignIn.Id }),
            seeded.PersonalSignIn.NormalizedEmail, false, "/login",
            "Sign in to your account", "Inicie sesión en su cuenta",
            "Someone tried to set up a personal account", "Alguien intentó configurar una cuenta personal",
            $"Someone tried to set up a personal account with your email. Your account already exists; sign in at {Origin}/login and set up your personal account there.",
            $"Alguien intentó configurar una cuenta personal con su correo electrónico. Su cuenta ya existe; inicie sesión en {Origin}/login y configure ahí su cuenta personal."),
        new("password recovery", "identity.password.recovery.requested",
            JsonSerializer.Serialize(new { RequestId = seeded.PasswordReset.Id }),
            seeded.Account.Email!, true, "/credentials/reset",
            "Reset your password", "Restablezca su contraseña",
            "Open this link to choose a new password:", "Abra este enlace para elegir una contraseña nueva:"),
        new("account reactivation request", "identity.reactivation.requested",
            JsonSerializer.Serialize(new { RequestId = seeded.Reactivation.Id }),
            seeded.Account.Email!, true, "/account/reactivate",
            "Reactivate your account", "Reactive su cuenta",
            "Open this link and enter your current password", "Abra este enlace e ingrese su contraseña actual"),
        new("self-deactivation notice", "identity.lifecycle.self.deactivated.notice.requested",
            JsonSerializer.Serialize(new { IdentityId = seeded.Account.Id }),
            seeded.Account.Email!, false, "/account/reactivation-request",
            "Your account was deactivated", "Su cuenta fue desactivada",
            "Your account was deactivated", "Su cuenta fue desactivada",
            $"Your account was deactivated and all your sessions were closed. To request reactivation, visit {Origin}/account/reactivation-request",
            $"Su cuenta fue desactivada y se cerraron todas sus sesiones. Para solicitar la reactivación, visite {Origin}/account/reactivation-request"),
        new("administrative suspension notice", "identity.lifecycle.administratively.suspended.notice.requested",
            JsonSerializer.Serialize(new { IdentityId = seeded.Account.Id }),
            seeded.Account.Email!, false, "/login",
            "Your account status changed", "El estado de su cuenta cambió",
            "An administrator suspended your account", "Un administrador suspendió su cuenta"),
        new("administrative reactivation notice", "identity.lifecycle.reactivated.notice.requested",
            JsonSerializer.Serialize(new { IdentityId = seeded.Account.Id }),
            seeded.Account.Email!, false, "/login",
            "Your account status changed", "El estado de su cuenta cambió",
            "An administrator lifted your account suspension", "Un administrador levantó la suspensión de su cuenta"),
        new("Platform owner invitation", "platform.invitation.requested",
            JsonSerializer.Serialize(new { InvitationId = seeded.PlatformOwner.Id.Value }),
            seeded.PlatformOwner.NormalizedEmail, true, "/platform/invitations/register",
            "You have been invited to Platform", "Tiene una invitación a la Plataforma",
            "You have been invited to become the Platform owner", "Recibió una invitación para asumir el rol de propietario de la Plataforma"),
        new("Platform administrator invitation", "platform.invitation.requested",
            JsonSerializer.Serialize(new { InvitationId = seeded.PlatformAdministrator.Id.Value }),
            seeded.PlatformAdministrator.NormalizedEmail, true, "/platform/invitations/register",
            "You have been invited to Platform", "Tiene una invitación a la Plataforma",
            "You have been invited to become a Platform administrator", "Recibió una invitación para asumir el rol de administrador de la Plataforma"),
        new("Platform confirmation", "platform.invitation.confirmation.requested",
            JsonSerializer.Serialize(new { IdentityId = seeded.PlatformAccount.Id, InvitationId = seeded.PlatformAdministrator.Id.Value }),
            seeded.PlatformAccount.Email!, true, "/platform/invitations/confirm",
            "Confirm your Platform email address", "Confirme su correo electrónico de la Plataforma",
            "Open this link to confirm your address:", "Abra este enlace para confirmar su dirección:",
            $"Open this link to confirm your address: {Origin}/platform/invitations/confirm#token={Uri.EscapeDataString(Token)}",
            $"Abra este enlace para confirmar su dirección: {Origin}/platform/invitations/confirm#token={Uri.EscapeDataString(Token)}"),
        new("Platform sign-in notice", "platform.invitation.signin.notice.requested",
            JsonSerializer.Serialize(new { IdentityId = seeded.PlatformAccount.Id, InvitationId = seeded.PlatformAdministrator.Id.Value }),
            seeded.PlatformAccount.Email!, false, "/login",
            "Sign in to your account", "Inicie sesión en su cuenta",
            "Someone tried to register with your email", "Alguien intentó registrarse con su correo electrónico")
    ];

    private sealed record Seeded(
        ApplicationUser Account,
        ApplicationUser InvitedAccount,
        ApplicationUser PlatformAccount,
        ApplicationUser OrganizationIntentAccount,
        ApplicationUser PersonalIntentAccount,
        Invitation OrganizationInvitation,
        PendingRegistrationIntent OrganizationConfirmation,
        PendingRegistrationIntent OrganizationSignIn,
        PendingPersonalIntent PersonalConfirmation,
        PendingPersonalIntent PersonalSignIn,
        PasswordResetRequest PasswordReset,
        AccountReactivationRequest Reactivation,
        PlatformAdminInvitation PlatformOwner,
        PlatformAdminInvitation PlatformAdministrator);

    private sealed record EmailCase(
        string Name,
        string MessageType,
        string Payload,
        string Recipient,
        bool RequiresSecret,
        string Path,
        string EnglishSubject,
        string SpanishSubject,
        string EnglishLead,
        string SpanishLead,
        string? EnglishBody = null,
        string? SpanishBody = null);

}

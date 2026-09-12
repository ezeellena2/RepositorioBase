using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.Common.Exceptions;
using CleanArchitecture.Application.Common.Validation;
using CleanArchitecture.Application.FunctionalTests.Infrastructure;
using CleanArchitecture.Application.IdentityAccess.Organizations.RegisterOrganization;
using CleanArchitecture.Domain.IdentityAccess.Auditing;
using CleanArchitecture.Domain.IdentityAccess.Memberships;
using CleanArchitecture.Domain.IdentityAccess.Organizations;
using CleanArchitecture.Domain.IdentityAccess.Outbox;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using CleanArchitecture.Infrastructure.Identity;
using CleanArchitecture.Web.Infrastructure;
using Microsoft.AspNetCore.Http;
using NUnit.Framework;
using Shouldly;

namespace CleanArchitecture.Application.FunctionalTests.IdentityAccess.Organizations;

public sealed class RegistrationInputValidationTests : TestBase
{
    public static IEnumerable<TestCaseData> InvalidCommands()
    {
        yield return new TestCaseData(new RegisterOrganizationCommand(null!, "Testing1234!", "Northwind", "30-12345678-1")).SetName("Null_email_is_rejected");
        yield return new TestCaseData(new RegisterOrganizationCommand("not-an-email", "Testing1234!", "Northwind", "30-12345678-1")).SetName("Malformed_email_is_rejected");
        yield return new TestCaseData(new RegisterOrganizationCommand("owner@example.test", null!, "Northwind", "30-12345678-1")).SetName("Null_password_is_rejected");
        yield return new TestCaseData(new RegisterOrganizationCommand("owner@example.test", "Testing1234!", null!, "30-12345678-1")).SetName("Null_legal_name_is_rejected");
        yield return new TestCaseData(new RegisterOrganizationCommand("owner@example.test", "Testing1234!", "Northwind", "not-a-cuit")).SetName("Malformed_cuit_is_rejected");
        yield return new TestCaseData(new RegisterOrganizationCommand($"{new string('a', 320)}@example.test", "Testing1234!", "Northwind", "30-12345678-1")).SetName("Oversized_email_is_rejected");
        yield return new TestCaseData(new RegisterOrganizationCommand("owner@example.test", new string('P', 512), "Northwind", "30-12345678-1")).SetName("Oversized_password_is_rejected");
        yield return new TestCaseData(new RegisterOrganizationCommand("owner@example.test", "Testing1234!", new string('N', 512), "30-12345678-1")).SetName("Oversized_legal_name_is_rejected");
    }

    public static IEnumerable<TestCaseData> InvalidSessionCommands()
    {
        yield return new TestCaseData(true, null, string.Empty, string.Empty)
            .SetName("Invalid_session_outranks_blank_registration_credentials");
        yield return new TestCaseData(false, "owner@example.test", string.Empty, string.Empty)
            .SetName("Email_only_session_outranks_blank_registration_credentials");
        yield return new TestCaseData(false, "owner@example.test", "not-an-email", new string('P', 512))
            .SetName("Email_only_session_outranks_malformed_registration_credentials");
    }

    [TestCaseSource(nameof(InvalidCommands))]
    public async Task Invalid_registration_input_is_a_safe_failure_without_durable_effects(RegisterOrganizationCommand command)
    {
        var exception = Assert.ThrowsAsync<ValidationException>(async () => await TestApp.SendAsync(command));

        exception!.Errors.Keys.ShouldAllBe(key => char.IsLower(key[0]));
        await AssertNoRegistrationEffectsAsync();
    }

    [TestCaseSource(nameof(InvalidSessionCommands))]
    public async Task Session_failure_outranks_anonymous_credential_validation_without_durable_effects(
        bool isInvalid,
        string? sessionEmail,
        string requestEmail,
        string password)
    {
        TestApp.SetValidatedOptionalSession(null, sessionEmail, isInvalid);

        var result = await TestApp.SendAsync(new RegisterOrganizationCommand(
            requestEmail,
            password,
            "Northwind",
            "30-12345678-1"));

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("invalid_session");
        result.Error.Category.ShouldBe(ApplicationErrorCategory.Authentication);
        ApiProblemDetailsMapper.GetStatusCode(result.Error.Category).ShouldBe(StatusCodes.Status401Unauthorized);
        await AssertNoRegistrationEffectsAsync();
    }

    [Test]
    public async Task Password_policy_refusal_is_field_indexed_without_durable_effects()
    {
        var result = await TestApp.SendAsync(new RegisterOrganizationCommand(
            "owner@example.test",
            "weak",
            "Northwind",
            "30-12345678-1"));

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("validation_failed");
        result.Error.Category.ShouldBe(ApplicationErrorCategory.Validation);
        result.Error.ValidationErrors.Keys.ShouldBe(["password"]);
        var policy = result.Error.ValidationErrors["password"].ShouldHaveSingleItem();
        policy.Code.ShouldBe(ValidationErrorCodes.PasswordPolicy);
        policy.Params.ShouldBeEmpty();
        System.Text.Json.JsonSerializer.Serialize(result.Error.ValidationErrors).ShouldNotContain("weak");
        System.Text.Json.JsonSerializer.Serialize(result.Error.ValidationErrors).ShouldNotContain("Passwords must");
        await AssertNoRegistrationEffectsAsync();
    }

    [Test]
    public async Task Signed_in_registration_derives_the_owner_from_the_session_when_credentials_are_omitted()
    {
        var email = $"owner-{Guid.NewGuid():N}@example.test";
        var identityId = await TestApp.RunAsUserAsync(email, "Testing1234!", []);
        TestApp.SetValidatedOptionalSession(identityId, email);

        var result = await TestApp.SendAsync(new RegisterOrganizationCommand(
            string.Empty,
            string.Empty,
            "Northwind",
            "30-12345678-1"));

        result.IsSuccess.ShouldBeTrue();
        (await TestApp.CountAsync<ApplicationUser>()).ShouldBe(1);
        (await TestApp.CountAsync<OrganizationProfile>()).ShouldBe(1);
        (await TestApp.ListAsync<TenantMembership>()).Single().IdentityId.ShouldBe(identityId);
    }

    [Test]
    public async Task Signed_in_registration_treats_whitespace_email_as_omitted_and_ignores_the_unused_password()
    {
        var email = $"owner-{Guid.NewGuid():N}@example.test";
        var identityId = await TestApp.RunAsUserAsync(email, "Testing1234!", []);
        TestApp.SetValidatedOptionalSession(identityId, email);

        var result = await TestApp.SendAsync(new RegisterOrganizationCommand(
            new string(' ', 512),
            new string('p', 512),
            "Northwind",
            "30-12345678-1"));

        result.IsSuccess.ShouldBeTrue();
        (await TestApp.CountAsync<ApplicationUser>()).ShouldBe(1);
        (await TestApp.CountAsync<OrganizationProfile>()).ShouldBe(1);
        (await TestApp.ListAsync<TenantMembership>()).Single().IdentityId.ShouldBe(identityId);
    }

    [Test]
    public async Task Signed_in_registration_rejects_an_explicit_spoofed_email_and_creates_nothing()
    {
        var email = $"owner-{Guid.NewGuid():N}@example.test";
        var identityId = await TestApp.RunAsUserAsync(email, "Testing1234!", []);
        TestApp.SetValidatedOptionalSession(identityId, email);

        var result = await TestApp.SendAsync(new RegisterOrganizationCommand(
            "somebody-else@example.test",
            new string('p', 512),
            "Northwind",
            "30-12345678-1"));

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("invalid_registration");
        (await TestApp.CountAsync<ApplicationUser>()).ShouldBe(1);
        (await TestApp.CountAsync<RegistrationSubmission>()).ShouldBe(0);
        (await TestApp.CountAsync<OrganizationProfile>()).ShouldBe(0);
        (await TestApp.CountAsync<TenantMembership>()).ShouldBe(0);
        (await TestApp.CountAsync<OutboxMessage>()).ShouldBe(0);
    }

    /// <summary>
    /// The counterpart to the refusals above, so they cannot pass by refusing everything: valid input still writes
    /// the durable record of the request. What an unproved request may write is the intent and the message that
    /// carries its proof, and nothing else.
    /// </summary>
    [Test]
    public async Task Valid_registration_input_still_records_the_registration_intent()
    {
        var result = await TestApp.SendAsync(new RegisterOrganizationCommand("owner@example.test", "Testing1234!", "Northwind", "30-12345678-1"));

        result.IsSuccess.ShouldBeTrue();
        (await TestApp.CountAsync<RegistrationSubmission>()).ShouldBe(1);
        (await TestApp.CountAsync<PendingRegistrationIntent>()).ShouldBe(1);
        (await TestApp.CountAsync<OutboxMessage>()).ShouldBe(1);
        (await TestApp.CountAsync<ApplicationUser>()).ShouldBe(0);
        (await TestApp.CountAsync<Tenant>()).ShouldBe(0);
    }

    private static async Task AssertNoRegistrationEffectsAsync()
    {
        (await TestApp.CountAsync<RegistrationSubmission>()).ShouldBe(0);
        (await TestApp.CountAsync<PendingRegistrationIntent>()).ShouldBe(0);
        (await TestApp.CountAsync<ApplicationUser>()).ShouldBe(0);
        (await TestApp.CountAsync<Tenant>()).ShouldBe(0);
        (await TestApp.CountAsync<OrganizationProfile>()).ShouldBe(0);
        (await TestApp.CountAsync<TenantMembership>()).ShouldBe(0);
        (await TestApp.CountAsync<OutboxMessage>()).ShouldBe(0);
        (await TestApp.CountAsync<OutboxSecret>()).ShouldBe(0);
        (await TestApp.CountAsync<AuditEvent>()).ShouldBe(0);
    }
}

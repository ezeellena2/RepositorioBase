using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.FunctionalTests.Infrastructure;
using CleanArchitecture.Application.IdentityAccess.Organizations.RegisterOrganization;
using CleanArchitecture.Domain.IdentityAccess.Auditing;
using CleanArchitecture.Domain.IdentityAccess.Memberships;
using CleanArchitecture.Domain.IdentityAccess.Organizations;
using CleanArchitecture.Domain.IdentityAccess.Outbox;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using CleanArchitecture.Infrastructure.Identity;
using NUnit.Framework;
using Shouldly;

namespace CleanArchitecture.Application.FunctionalTests.IdentityAccess.Organizations;

public sealed class RegistrationInputValidationTests : TestBase
{
    public static IEnumerable<TestCaseData> InvalidCommands()
    {
        yield return new TestCaseData(new RegisterOrganizationCommand(null!, "Testing1234!", "Northwind", "30-12345678-9")).SetName("Null_email_is_rejected");
        yield return new TestCaseData(new RegisterOrganizationCommand("not-an-email", "Testing1234!", "Northwind", "30-12345678-9")).SetName("Malformed_email_is_rejected");
        yield return new TestCaseData(new RegisterOrganizationCommand("owner@example.test", null!, "Northwind", "30-12345678-9")).SetName("Null_password_is_rejected");
        yield return new TestCaseData(new RegisterOrganizationCommand("owner@example.test", "weak", "Northwind", "30-12345678-9")).SetName("Weak_password_is_rejected");
        yield return new TestCaseData(new RegisterOrganizationCommand("owner@example.test", "Testing1234!", null!, "30-12345678-9")).SetName("Null_legal_name_is_rejected");
        yield return new TestCaseData(new RegisterOrganizationCommand("owner@example.test", "Testing1234!", "Northwind", "not-a-cuit")).SetName("Malformed_cuit_is_rejected");
        yield return new TestCaseData(new RegisterOrganizationCommand($"{new string('a', 320)}@example.test", "Testing1234!", "Northwind", "30-12345678-9")).SetName("Oversized_email_is_rejected");
        yield return new TestCaseData(new RegisterOrganizationCommand("owner@example.test", new string('P', 512), "Northwind", "30-12345678-9")).SetName("Oversized_password_is_rejected");
        yield return new TestCaseData(new RegisterOrganizationCommand("owner@example.test", "Testing1234!", new string('N', 512), "30-12345678-9")).SetName("Oversized_legal_name_is_rejected");
    }

    [TestCaseSource(nameof(InvalidCommands))]
    public async Task Invalid_registration_input_is_a_safe_failure_without_durable_effects(RegisterOrganizationCommand command)
    {
        var result = await TestApp.SendAsync(command);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("invalid_registration");
        await AssertNoRegistrationEffectsAsync();
    }

    [Test]
    public async Task Valid_registration_input_still_creates_the_registration_graph()
    {
        var result = await TestApp.SendAsync(new RegisterOrganizationCommand("owner@example.test", "Testing1234!", "Northwind", "30-12345678-9"));

        result.IsSuccess.ShouldBeTrue();
        (await TestApp.CountAsync<RegistrationSubmission>()).ShouldBe(1);
        (await TestApp.CountAsync<ApplicationUser>()).ShouldBe(1);
        (await TestApp.CountAsync<Tenant>()).ShouldBe(1);
    }

    private static async Task AssertNoRegistrationEffectsAsync()
    {
        (await TestApp.CountAsync<RegistrationSubmission>()).ShouldBe(0);
        (await TestApp.CountAsync<ApplicationUser>()).ShouldBe(0);
        (await TestApp.CountAsync<Tenant>()).ShouldBe(0);
        (await TestApp.CountAsync<OrganizationProfile>()).ShouldBe(0);
        (await TestApp.CountAsync<TenantMembership>()).ShouldBe(0);
        (await TestApp.CountAsync<OutboxMessage>()).ShouldBe(0);
        (await TestApp.CountAsync<OutboxSecret>()).ShouldBe(0);
        (await TestApp.CountAsync<AuditEvent>()).ShouldBe(0);
    }
}

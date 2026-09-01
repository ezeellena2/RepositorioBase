using System.Reflection;
using NUnit.Framework;
using Shouldly;

namespace CleanArchitecture.Application.UnitTests.Architecture;

public sealed class RegistrationApplicationShapeTests
{
    [TestCase("CleanArchitecture.Application.IdentityAccess.Organizations.RegisterOrganization.RegisterOrganizationCommand")]
    [TestCase("CleanArchitecture.Application.IdentityAccess.Organizations.ConfirmEmail.ConfirmEmailCommand")]
    [TestCase("CleanArchitecture.Application.IdentityAccess.Organizations.RegisterOrganization.IRegistrationIdempotencyStore")]
    [TestCase("CleanArchitecture.Application.IdentityAccess.Sessions.IValidatedOptionalSession")]
    public void Application_registration_contract_types_exist(string typeName) =>
        typeof(CleanArchitecture.Application.Common.Security.AuthorizeAttribute).Assembly.GetType(typeName).ShouldNotBeNull(typeName);

    [TestCase("CleanArchitecture.Domain.IdentityAccess.Organizations.RegistrationSubmission")]
    [TestCase("CleanArchitecture.Domain.IdentityAccess.Outbox.OutboxMessage")]
    [TestCase("CleanArchitecture.Domain.IdentityAccess.Outbox.OutboxSecret")]
    public void Domain_registration_contract_types_exist(string typeName) =>
        typeof(CleanArchitecture.Domain.IdentityAccess.Tenants.Tenant).Assembly.GetType(typeName).ShouldNotBeNull(typeName);

    [Test]
    public void Outbox_contract_exposes_delivery_and_secret_evidence()
    {
        var assembly = typeof(CleanArchitecture.Domain.IdentityAccess.Tenants.Tenant).Assembly;
        var message = assembly.GetType("CleanArchitecture.Domain.IdentityAccess.Outbox.OutboxMessage")!;
        var secret = assembly.GetType("CleanArchitecture.Domain.IdentityAccess.Outbox.OutboxSecret")!;

        foreach (var member in new[] { "AttemptCount", "NextAttemptAt", "FailureCode" })
        {
            message.GetProperty(member).ShouldNotBeNull(member);
        }
        foreach (var member in new[] { "ExpiresAt", "Status", "TerminalReason", "Ciphertext", "ProviderReceipt", "CompletedAt" })
        {
            secret.GetProperty(member).ShouldNotBeNull(member);
        }
    }

    [Test]
    public void Requests_with_secret_bearing_members_are_explicitly_classified_as_sensitive()
    {
        var applicationAssembly = typeof(CleanArchitecture.Application.Common.Security.AuthorizeAttribute).Assembly;
        var unclassified = ApplicationRequestInventory.GetConcreteRequests(applicationAssembly)
            .Where(type => type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Any(property => property.Name.Contains("password", StringComparison.OrdinalIgnoreCase) ||
                                 property.Name.Contains("token", StringComparison.OrdinalIgnoreCase) ||
                                 property.Name.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
                                 property.Name.Contains("credential", StringComparison.OrdinalIgnoreCase)))
            .Where(type => !type.GetInterfaces().Any(@interface => @interface.Name == "ISensitiveRequest"))
            .Select(type => type.FullName)
            .ToArray();

        unclassified.ShouldBeEmpty("secret-bearing application requests must be classified so they remain covered by the centralized no-payload logging policy");
    }
}

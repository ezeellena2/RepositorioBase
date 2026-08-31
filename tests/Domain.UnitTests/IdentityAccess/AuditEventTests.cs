using CleanArchitecture.Domain.IdentityAccess.Auditing;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using NUnit.Framework;
using Shouldly;

namespace CleanArchitecture.Domain.UnitTests.IdentityAccess;

public class AuditEventTests
{
    [Test]
    public void AuditEventRequiresCorrelationAndCopiesAllowlistedMetadata()
    {
        var metadata = new Dictionary<string, string>
        {
            ["reason"] = "email-confirmed",
            ["code"] = "identity.confirmed"
        };

        var auditEvent = AuditEvent.Create(
            TenantId.New(),
            Guid.NewGuid(),
            "identity.confirmed",
            "corr-123",
            metadata);

        metadata["reason"] = "mutated-by-caller";

        auditEvent.CorrelationId.ShouldBe("corr-123");
        auditEvent.Metadata["reason"].ShouldBe("email-confirmed");
        auditEvent.Metadata["code"].ShouldBe("identity.confirmed");
    }

    [Test]
    public void AuditEventRejectsMissingCorrelationAndSecretMetadata()
    {
        Should.Throw<ArgumentException>(() => AuditEvent.Create(default, null, "identity.confirmed", "corr-empty-tenant"));
        Should.Throw<ArgumentException>(() => AuditEvent.Create(TenantId.New(), null, "identity.confirmed", " "));
        Should.Throw<ArgumentException>(() => AuditEvent.Create(
            TenantId.New(),
            null,
            "identity.confirmed",
            "corr-456",
            new Dictionary<string, string> { ["password"] = "not-for-audit" }));
        Should.Throw<ArgumentException>(() => AuditEvent.Create(
            TenantId.New(),
            null,
            "identity.confirmed",
            "corr-789",
            new Dictionary<string, string> { ["reason"] = "token=secret" }));
    }

    [Test]
    public void AuditEventRejectsOtpAndRecoveryOrConfirmationCodeMaterial()
    {
        Should.Throw<ArgumentException>(() => AuditEvent.Create(
            TenantId.New(),
            null,
            "identity.confirmed",
            "corr-otp",
            new Dictionary<string, string> { ["code"] = "123456" }));
        Should.Throw<ArgumentException>(() => AuditEvent.Create(
            TenantId.New(),
            null,
            "identity.confirmed",
            "corr-recovery",
            new Dictionary<string, string> { ["reason"] = "recovery code 123456" }));
        Should.Throw<ArgumentException>(() => AuditEvent.Create(
            TenantId.New(),
            null,
            "identity.confirmed",
            "corr-confirmation",
            new Dictionary<string, string> { ["reason"] = "confirmation code 874321" }));
    }

    [Test]
    public void AuditEventIdentityCannotBeExternallyMutated()
    {
        var idProperty = typeof(AuditEvent).GetProperty(nameof(AuditEvent.Id));

        idProperty.ShouldNotBeNull();
        idProperty!.SetMethod.ShouldNotBeNull();
        idProperty.SetMethod!.IsPublic.ShouldBeFalse();
    }
}

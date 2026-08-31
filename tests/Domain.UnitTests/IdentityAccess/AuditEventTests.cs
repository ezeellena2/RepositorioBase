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

    [TestCase("code", "otp-123456")]
    [TestCase("code", "recovery_code_874321")]
    [TestCase("outcome", "confirmation-code-654321")]
    [TestCase("reason", "recovery-code: 123456")]
    [TestCase("reason", "confirmation code A1B2C3")]
    public void AuditEventRejectsEncodedVerificationCodeMaterial(string key, string value)
    {
        Should.Throw<ArgumentException>(() => AuditEvent.Create(
            TenantId.New(),
            null,
            "identity.confirmed",
            "corr-encoded-code",
            new Dictionary<string, string> { [key] = value }));
    }

    [TestCase("reason", "confirmation code expired")]
    [TestCase("code", "identity.confirmed")]
    [TestCase("outcome", "success")]
    public void AuditEventRetainsSafeSemanticMetadata(string key, string value)
    {
        var auditEvent = AuditEvent.Create(
            TenantId.New(),
            null,
            "identity.confirmed",
            "corr-safe-metadata",
            new Dictionary<string, string> { [key] = value });

        auditEvent.Metadata[key].ShouldBe(value);
    }
}

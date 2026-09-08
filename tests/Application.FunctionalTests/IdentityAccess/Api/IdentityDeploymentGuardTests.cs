using CleanArchitecture.Application.FunctionalTests.Infrastructure;

namespace CleanArchitecture.Application.FunctionalTests.IdentityAccess.Api;

/// <summary>
/// The configurations a deployment is not allowed to start in the middle of (IA-REQ-055, IA-REQ-056).
/// <para>
/// Both are operator intentions contradicted by what sits beside them, and both would otherwise leave a process
/// that looks healthy. A deployment armed for restore without a verification key answers `503` to every route
/// for want of one setting; a deployment holding real personal data with no retention policy erases nothing, and
/// "erases nothing" is a decision somebody has to have taken rather than one nobody noticed.
/// </para>
/// <para>
/// Each test starts the real host. A configuration check that only read the settings would prove that this file
/// agrees with itself, which is not the question.
/// </para>
/// </summary>
public sealed class IdentityDeploymentGuardTests : TestBase
{
    [Test]
    public void A_deployment_armed_for_restore_without_a_verification_key_refuses_to_start()
    {
        using var factory = Guarded(new Dictionary<string, string?>
        {
            ["IdentityAccess:Recovery:Deployment"] = "restored-deployment"
        });

        var refusal = Should.Throw<InvalidOperationException>(() => factory.CreateClient());

        refusal.Message.ShouldContain("VerificationKey");
        refusal.Message.Contains("restored-deployment", StringComparison.Ordinal)
            .ShouldBeFalse("a refusal names what is missing, never a configured value");
    }

    [Test]
    public void A_deployment_armed_for_restore_with_a_key_starts_and_decides_admission_the_usual_way()
    {
        using var factory = Guarded(new Dictionary<string, string?>
        {
            ["IdentityAccess:Recovery:Deployment"] = "restored-deployment",
            ["IdentityAccess:Recovery:VerificationKey"] = Convert.ToBase64String(new byte[32])
        });

        // It starts. What it then admits is the adapter's business and is closed here, because no evidence was
        // supplied — but that is a `503` an operator can read, not a process that would not come up.
        Should.NotThrow(() => factory.CreateClient());
    }

    [Test]
    public void A_deployment_holding_real_personal_data_with_no_retention_policy_refuses_to_start()
    {
        using var factory = Guarded(new Dictionary<string, string?>
        {
            ["IdentityAccess:PersonalData:Mode"] = "Real"
        });

        var refusal = Should.Throw<InvalidOperationException>(() => factory.CreateClient());

        refusal.Message.ShouldContain("retention policy");
    }

    [Test]
    public void A_deployment_holding_real_personal_data_with_a_policy_and_keys_starts()
    {
        using var factory = Guarded(new Dictionary<string, string?>
        {
            ["IdentityAccess:PersonalData:Mode"] = "Real",
            ["IdentityAccess:RetentionPolicy:PolicyId"] = "RET-TEST-01",
            ["IdentityAccess:RetentionPolicy:Version"] = "1",
            ["IdentityAccess:RetentionPolicy:Owner"] = "platform-operations",
            ["IdentityAccess:RetentionPolicy:ApprovedOn"] = "2026-01-15",
            ["IdentityAccess:RetentionPolicy:Source"] = "docs/policies/retention-test.md",
            ["IdentityAccess:RetentionPolicy:BackupTreatment"] = "restored copies are re-purged before admission",
            ["IdentityAccess:RetentionPolicy:Categories:0:Category"] = "SessionRecords",
            ["IdentityAccess:RetentionPolicy:Categories:0:RetentionPeriod"] = "P90D",
            ["IdentityAccess:RetentionPolicy:Categories:0:Trigger"] = "LastActivity",
            ["IdentityAccess:RetentionPolicy:Categories:0:Action"] = "Erase",
            ["IdentityAccess:RetentionPolicy:Categories:0:EvidenceRequired"] = "true"
        });

        Should.NotThrow(() => factory.CreateClient());
    }

    /// <summary>
    /// The document key is supplied by the harness for every scenario, so taking it away is how this test asks
    /// what a real deployment that forgot it would meet.
    /// </summary>
    [Test]
    public void A_deployment_holding_real_personal_data_with_no_document_key_refuses_to_start()
    {
        using var factory = Guarded(new Dictionary<string, string?>
        {
            ["IdentityAccess:PersonalData:Mode"] = "Real",
            ["IdentityAccess:RetentionPolicy:PolicyId"] = "RET-TEST-01",
            ["IdentityAccess:RetentionPolicy:Version"] = "1",
            ["IdentityAccess:RetentionPolicy:Owner"] = "platform-operations",
            ["IdentityAccess:RetentionPolicy:ApprovedOn"] = "2026-01-15",
            ["IdentityAccess:RetentionPolicy:Source"] = "docs/policies/retention-test.md",
            ["IdentityAccess:RetentionPolicy:BackupTreatment"] = "restored copies are re-purged before admission",
            ["IdentityAccess:RetentionPolicy:Categories:0:Category"] = "SessionRecords",
            ["IdentityAccess:RetentionPolicy:Categories:0:RetentionPeriod"] = "P90D",
            ["IdentityAccess:RetentionPolicy:Categories:0:Trigger"] = "LastActivity",
            ["IdentityAccess:RetentionPolicy:Categories:0:Action"] = "Erase",
            ["IdentityAccess:RetentionPolicy:Categories:0:EvidenceRequired"] = "true",
            ["IdentityAccess:People:DocumentProtection:CurrentKeyVersion"] = "0"
        });

        var refusal = Should.Throw<InvalidOperationException>(() => factory.CreateClient());

        refusal.Message.ShouldContain("DocumentProtection");
    }

    [Test]
    public void An_ordinary_synthetic_deployment_starts_with_nothing_configured_at_all()
    {
        using var factory = Guarded(new Dictionary<string, string?>());

        Should.NotThrow(() => factory.CreateClient());
    }

    private static WebApiFactory Guarded(IReadOnlyDictionary<string, string?> settings) =>
        new(FunctionalTestSetup.ConnectionString,
            Microsoft.Extensions.Hosting.Environments.Production,
            useTestAuthentication: false,
            useTestIdentityAccessDoubles: false,
            settings: settings,
            keepDeploymentGuard: true);
}

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CleanArchitecture.Application.IdentityAccess.Lifecycle;
using CleanArchitecture.Infrastructure.IdentityAccess.Lifecycle;
using Microsoft.Extensions.Configuration;

namespace CleanArchitecture.Infrastructure.IntegrationTests.IdentityAccess;

/// <summary>
/// What a restored deployment admits, and — far more often — what it does not (IA-REQ-055).
/// <para>
/// Every path through this guard except one ends Closed. That is the design: absent, unreadable, expired,
/// wrongly signed, wrong-deployment and non-advancing evidence are six different mistakes with one answer, and
/// the tests are mostly here to prove that none of them found a seventh answer.
/// </para>
/// <para>
/// **What these prove and what they do not.** They prove the adapter refuses everything it should against
/// evidence a test produced. They are not restore certification: no backup was taken, none was restored, and no
/// external authority issued anything. That gate has an owner outside this repository.
/// </para>
/// </summary>
public sealed class RestoreAdmissionTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);
    private const string Deployment = "identity-access-local";
    private static readonly string Key = Convert.ToBase64String(Encoding.UTF8.GetBytes("an-operator-key-nobody-put-in-a-backup"));

    [Test]
    public void A_deployment_nobody_armed_is_not_recovering_and_admits_what_it_always_did()
    {
        var admission = Admission([]);

        admission.State.ShouldBe(RecoveryAdmissionState.Open);
        admission.Reason.ShouldBe(RecoveryAdmissionReason.NotRecovering);
        admission.AdmitsPublicIngress.ShouldBeTrue();
    }

    /// <summary>
    /// Naming a deployment arms it, and nothing else in the configuration can talk it back down. An operator who
    /// deployed the evidence and forgot the key gets a refusing deployment, not an open one.
    /// </summary>
    [Test]
    public void An_armed_deployment_that_cannot_verify_anything_is_closed()
    {
        var admission = Admission([
            new("IdentityAccess:Recovery:Deployment", Deployment),
            new("IdentityAccess:Recovery:Evidence", Signed(release: true))
        ]);

        admission.State.ShouldBe(RecoveryAdmissionState.Closed);
        admission.Reason.ShouldBe(RecoveryAdmissionReason.EvidenceInvalid);
    }

    [Test]
    public void An_armed_deployment_with_no_evidence_at_all_is_closed()
    {
        var admission = Admission(Armed());

        admission.State.ShouldBe(RecoveryAdmissionState.Closed);
        admission.Reason.ShouldBe(RecoveryAdmissionReason.EvidenceMissing);
        admission.AdmitsPublicIngress.ShouldBeFalse();
        admission.AdmitsDelivery.ShouldBeFalse();
    }

    [Test]
    public void Evidence_pointing_at_a_file_that_is_not_there_is_evidence_that_is_not_there()
    {
        var admission = Admission(Armed(("EvidenceFile", Path.Combine(Path.GetTempPath(), $"absent-{Guid.NewGuid():N}.json"))));

        admission.State.ShouldBe(RecoveryAdmissionState.Closed);
        admission.Reason.ShouldBe(RecoveryAdmissionReason.EvidenceMissing);
    }

    [Test]
    public void Verified_evidence_without_the_release_claim_buys_quarantine_and_no_more()
    {
        var admission = Admission(Armed(("Evidence", Signed(release: false))));

        admission.State.ShouldBe(RecoveryAdmissionState.Quarantined);
        admission.Reason.ShouldBe(RecoveryAdmissionReason.Verified);
        admission.AdmitsPublicIngress.ShouldBeTrue("authentication and revalidation are what quarantine is for");
        admission.AdmitsDelivery.ShouldBeFalse("nothing is delivered until an operator releases it");
    }

    [Test]
    public void Only_the_release_claim_opens_a_deployment()
    {
        var admission = Admission(Armed(("Evidence", Signed(release: true))));

        admission.State.ShouldBe(RecoveryAdmissionState.Open);
        admission.AdmitsDelivery.ShouldBeTrue();
    }

    /// <summary>
    /// Anything the restored database holds from before the stamp is a thing from before the restore, and being
    /// from before the restore is the whole reason not to honour it.
    /// </summary>
    [Test]
    public void Everything_stamped_before_the_epoch_is_from_before_the_restore()
    {
        var admission = Admission(Armed(("Evidence", Signed(release: false))));

        admission.PredatesRecovery(Now.AddMinutes(-1)).ShouldBeTrue();
        admission.PredatesRecovery(Now.AddMinutes(1)).ShouldBeFalse();
    }

    [Test]
    public void Evidence_whose_window_has_closed_is_stale_and_closed()
    {
        var admission = Admission(Armed(("Evidence", Signed(release: true, expiresAt: Now.AddMinutes(-1)))));

        admission.State.ShouldBe(RecoveryAdmissionState.Closed);
        admission.Reason.ShouldBe(RecoveryAdmissionReason.EvidenceStale);
    }

    /// <summary>
    /// The most dangerous thing this can be handed: a record that is genuine, correctly signed, and about
    /// somewhere else. Everything about it looks right.
    /// </summary>
    [Test]
    public void Correctly_signed_evidence_about_another_deployment_opens_nothing()
    {
        var admission = Admission(Armed(("Evidence", Signed(release: true, deployment: "somewhere-else"))));

        admission.State.ShouldBe(RecoveryAdmissionState.Closed);
        admission.Reason.ShouldBe(RecoveryAdmissionReason.EvidenceInvalid);
    }

    [Test]
    public void Evidence_signed_with_the_wrong_key_opens_nothing()
    {
        var other = Convert.ToBase64String(Encoding.UTF8.GetBytes("a key somebody found in a backup"));

        var admission = Admission(Armed(("Evidence", Signed(release: true, signingKey: other))));

        admission.State.ShouldBe(RecoveryAdmissionState.Closed);
        admission.Reason.ShouldBe(RecoveryAdmissionReason.EvidenceInvalid);
    }

    /// <summary>
    /// A record with no signature at all, and one whose fields were edited after signing. Both are the same
    /// mistake — somebody wrote the document themselves — and both answer the same way.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public void Evidence_nobody_signed_or_somebody_edited_opens_nothing(bool signedThenEdited)
    {
        var evidence = signedThenEdited
            ? Signed(release: false).Replace("\"Release\":false", "\"Release\":true", StringComparison.Ordinal)
            : Unsigned();

        var admission = Admission(Armed(("Evidence", evidence)));

        admission.State.ShouldBe(RecoveryAdmissionState.Closed);
        admission.Reason.ShouldBe(RecoveryAdmissionReason.EvidenceInvalid);
    }

    [Test]
    public void Evidence_that_is_not_a_document_at_all_opens_nothing()
    {
        var admission = Admission(Armed(("Evidence", "this is not json")));

        admission.State.ShouldBe(RecoveryAdmissionState.Closed);
        admission.Reason.ShouldBe(RecoveryAdmissionReason.EvidenceInvalid);
    }

    /// <summary>
    /// The decision is taken again on every start, not once and remembered. Two constructions over the same
    /// configuration are two starts, and evidence that has expired between them closes the second.
    /// </summary>
    [Test]
    public void The_same_deployment_starting_again_asks_again()
    {
        var settings = Armed(("Evidence", Signed(release: true, expiresAt: Now.AddMinutes(30))));

        var first = Admission(settings, Now);
        var later = Admission(settings, Now.AddHours(1));

        first.State.ShouldBe(RecoveryAdmissionState.Open);
        later.State.ShouldBe(RecoveryAdmissionState.Closed, "a later start reads the same evidence and finds it stale");
        later.Reason.ShouldBe(RecoveryAdmissionReason.EvidenceStale);
    }

    /// <summary>
    /// Evidence read from a file outside the database, which is the shape an operator actually deploys: the
    /// record is a file this process is pointed at, and the key that verifies it is not in any backup.
    /// </summary>
    [Test]
    public void Evidence_held_outside_the_database_is_what_opens_it()
    {
        var path = Path.Combine(Path.GetTempPath(), $"recovery-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, Signed(release: true));
        try
        {
            Admission(Armed(("EvidenceFile", path))).State.ShouldBe(RecoveryAdmissionState.Open);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static RecoveryAdmission Admission(IEnumerable<KeyValuePair<string, string?>> settings, DateTimeOffset? now = null) =>
        new ConfiguredRecoveryAdmission(
            new ConfigurationBuilder().AddInMemoryCollection(settings).Build(),
            new FixedTime(now ?? Now)).Current;

    private static KeyValuePair<string, string?>[] Armed(params (string Key, string Value)[] extras) =>
    [
        new("IdentityAccess:Recovery:Deployment", Deployment),
        new("IdentityAccess:Recovery:VerificationKey", Key),
        .. extras.Select(extra => new KeyValuePair<string, string?>($"IdentityAccess:Recovery:{extra.Key}", extra.Value))
    ];

    /// <summary>The record an operator would issue, signed the way the adapter verifies it.</summary>
    private static string Signed(bool release, DateTimeOffset? expiresAt = null, string? deployment = null, string? signingKey = null)
    {
        var evidence = new RecoveryEvidence(
            deployment ?? Deployment,
            Now.ToString("O"),
            $"backup-{Guid.NewGuid():N}",
            Now.AddMinutes(-5).ToString("O"),
            (expiresAt ?? Now.AddHours(2)).ToString("O"),
            release,
            null);

        var signature = Convert.ToBase64String(HMACSHA256.HashData(
            Convert.FromBase64String(signingKey ?? Key),
            Encoding.UTF8.GetBytes(ConfiguredRecoveryAdmission.Canonical(evidence))));
        return JsonSerializer.Serialize(evidence with { Signature = signature });
    }

    private static string Unsigned() => JsonSerializer.Serialize(new RecoveryEvidence(
        Deployment, Now.ToString("O"), "backup-1", Now.ToString("O"), Now.AddHours(2).ToString("O"), true, null));

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

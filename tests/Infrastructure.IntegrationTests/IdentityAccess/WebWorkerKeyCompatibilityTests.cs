using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using CleanArchitecture.Infrastructure.IdentityAccess;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CleanArchitecture.Infrastructure.IntegrationTests.IdentityAccess;

/// <summary>
/// Whether the web application and the outbox worker can actually read each other's envelopes (IA-REQ-031).
/// <para>
/// They have to. The web application seals a confirmation token into an outbox message and the worker opens it to
/// send the mail; a key ring the two processes do not share turns every invitation into an envelope nobody can
/// open, and the symptom arrives hours later as mail that never went out.
/// </para>
/// <para>
/// Nothing here checks configuration. Two providers are built the way the two processes build theirs — separate
/// service collections, separate key ring caches, the same directory and the same certificate on disk — and then
/// one protects and the other unprotects. Configuration that matches is not evidence that the ciphertext travels.
/// </para>
/// </summary>
public sealed class WebWorkerKeyCompatibilityTests
{
    private const string Purpose = "IdentityAccess.OutboxSecret.v1";
    private const string Deployment = "identity-access-deployment";
    private const string CertificatePassword = "not-a-real-password";

    private string _keyRing = string.Empty;
    private string _certificate = string.Empty;

    [SetUp]
    public void Create_a_disposable_key_ring_and_a_certificate_of_its_own()
    {
        _keyRing = Directory.CreateTempSubdirectory("identity-keys").FullName;
        _certificate = Path.Combine(_keyRing, "wrapping.pfx");
        WriteCertificate(_certificate);
    }

    [TearDown]
    public void Remove_them()
    {
        if (Directory.Exists(_keyRing)) Directory.Delete(_keyRing, recursive: true);
    }

    [Test]
    public void What_the_web_application_seals_the_worker_opens()
    {
        using var web = Host_(Deployment, _certificate);
        using var worker = Host_(Deployment, _certificate);

        var sealed_ = Protector(web, Purpose).Protect("a confirmation token");

        Protector(worker, Purpose).Unprotect(sealed_)
            .ShouldBe("a confirmation token", "the two processes are one key ring or the mail never goes out");
    }

    /// <summary>
    /// The half a configuration check cannot see: the key ring on disk is encrypted with the wrapping
    /// certificate, so a process that starts later has to be able to decrypt it again. A deployment where only
    /// the process that wrote the keys can read them works until the first restart and then stops.
    /// </summary>
    [Test]
    public void A_process_that_starts_after_the_keys_were_written_can_still_read_them()
    {
        string sealed_;
        using (var first = Host_(Deployment, _certificate))
        {
            sealed_ = Protector(first, Purpose).Protect("written before the restart");
        }

        using var restarted = Host_(Deployment, _certificate);

        Protector(restarted, Purpose).Unprotect(sealed_)
            .ShouldBe("written before the restart", "a restart must not orphan the key ring it wrote");
    }

    [Test]
    public void A_new_key_in_the_ring_does_not_orphan_what_the_old_one_sealed()
    {
        using var before = Host_(Deployment, _certificate);
        var old = Protector(before, Purpose).Protect("sealed by the retiring key");

        // Rotation, done the way an operator does it: a new key is created and becomes the default for new
        // payloads, while every key already in the ring stays readable.
        using (var rotating = Host_(Deployment, _certificate))
        {
            rotating.Services.GetRequiredService<IKeyManager>().CreateNewKey(
                DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(90));
        }

        using var after = Host_(Deployment, _certificate);
        var fresh = Protector(after, Purpose).Protect("sealed by the new key");

        Protector(after, Purpose).Unprotect(old).ShouldBe("sealed by the retiring key");
        Protector(after, Purpose).Unprotect(fresh).ShouldBe("sealed by the new key");
    }

    [Test]
    public void A_different_discriminator_cannot_open_it()
    {
        using var web = Host_(Deployment, _certificate);
        using var stranger = Host_("some-other-deployment", _certificate);
        var sealed_ = Protector(web, Purpose).Protect("a confirmation token");

        Should.Throw<CryptographicException>(() => Protector(stranger, Purpose).Unprotect(sealed_));
    }

    [Test]
    public void A_different_purpose_cannot_open_it()
    {
        using var web = Host_(Deployment, _certificate);
        var sealed_ = Protector(web, Purpose).Protect("a confirmation token");

        Should.Throw<CryptographicException>(() => Protector(web, "IdentityAccess.SomethingElse.v1").Unprotect(sealed_));
    }

    /// <summary>
    /// A key ring wrapped by one certificate is not readable by a deployment holding another. That is the point
    /// of wrapping them, and it is also the failure an operator meets after replacing a certificate carelessly.
    /// <para>
    /// The second half of this test is the part worth knowing about. Data Protection does <em>not</em> refuse to
    /// start over a key ring it cannot decrypt: it skips the keys it cannot read and writes one of its own. So a
    /// deployment given the wrong certificate looks perfectly healthy — new mail is sealed and delivered — while
    /// every envelope already in flight is quietly dead. Nothing warns; the evidence is the failed deliveries.
    /// </para>
    /// </summary>
    [Test]
    public void A_different_certificate_cannot_open_what_the_right_one_sealed()
    {
        string sealed_;
        using (var web = Host_(Deployment, _certificate))
        {
            sealed_ = Protector(web, Purpose).Protect("a confirmation token");
        }

        var replacement = Path.Combine(_keyRing, "replacement.pfx");
        WriteCertificate(replacement);
        using var wrong = Host_(Deployment, replacement);

        Should.Throw<CryptographicException>(() => Protector(wrong, Purpose).Unprotect(sealed_));
        Protector(wrong, Purpose).Unprotect(Protector(wrong, Purpose).Protect("sealed after the replacement"))
            .ShouldBe("sealed after the replacement", "and it carries on as though nothing were wrong");
    }

    [Test]
    public void An_envelope_that_is_not_one_is_refused_rather_than_guessed_at()
    {
        using var worker = Host_(Deployment, _certificate);

        Should.Throw<CryptographicException>(() => Protector(worker, Purpose).Unprotect("not an envelope"));
    }

    /// <summary>A host built exactly the way both processes build theirs, over the settings they are given.</summary>
    private IHost Host_(string applicationName, string certificatePath)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["IdentityAccess:DataProtection:ApplicationName"] = applicationName,
            ["IdentityAccess:DataProtection:KeyRingPath"] = _keyRing,
            ["IdentityAccess:DataProtection:CertificatePath"] = certificatePath,
            ["IdentityAccess:DataProtection:CertificatePassword"] = CertificatePassword
        });
        builder.AddIdentityDataProtection();
        return builder.Build();
    }

    private static IDataProtector Protector(IHost host, string purpose) =>
        host.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector(purpose);

    private static void WriteCertificate(string path)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest($"CN=identity-access-test-{Guid.NewGuid():N}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        File.WriteAllBytes(path, certificate.Export(X509ContentType.Pkcs12, CertificatePassword));
    }
}

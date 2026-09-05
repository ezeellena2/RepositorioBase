using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Infrastructure.Data;
using CleanArchitecture.Infrastructure.Email;
using CleanArchitecture.Infrastructure.IdentityAccess;
using CleanArchitecture.Infrastructure.IntegrationTests.Infrastructure;
using CleanArchitecture.Infrastructure.IntegrationTests.TestDoubles;
using CleanArchitecture.Infrastructure.Outbox;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CleanArchitecture.Infrastructure.IntegrationTests.IdentityAccess;

public sealed class EmailConfigurationTests
{
    [Test]
    public async Task Configured_adapter_posts_to_Resend_with_the_exact_message_key()
    {
        var transport = new RecordingTransport(HttpStatusCode.OK, "{\"id\":\"5d205bd8-37df-4787-a5b5-8cde938d5bc9\"}");
        using var services = AdapterServices(transport);
        var sender = ActivatorUtilities.CreateInstance<IdentityEmailAdapter>(services);
        var id = Guid.NewGuid().ToString();
        var receipt = await sender.SendAsync("recipient@example.test", "Confirm", "https://app.example.test/confirm-email#token=private", id, CancellationToken.None);
        receipt.Delivered.ShouldBeTrue();
        receipt.ProviderReceipt.ShouldBe("5d205bd8-37df-4787-a5b5-8cde938d5bc9");
        transport.Uri.ShouldBe("https://api.resend.com/emails");
        transport.Key.ShouldBe(id);
        transport.Authorization.ShouldBe("Bearer isolated-test-key");
        transport.Body.ShouldNotBeNull();
        transport.Body!.ShouldContain("sender@example.test");
        transport.Body.ShouldContain("recipient@example.test");
        transport.Body.ShouldContain("https://app.example.test/confirm-email#token=private");
    }

    [TestCase(409, "concurrent_idempotent_requests", false)]
    [TestCase(409, "invalid_idempotent_request", true)]
    [TestCase(429, "rate_limit_exceeded", false)]
    [TestCase(503, "service_unavailable", false)]
    [TestCase(403, "validation_error", true)]
    public async Task Provider_errors_are_redacted_and_classified(int status, string code, bool permanent)
    {
        using var services = AdapterServices(new RecordingTransport((HttpStatusCode)status, $"{{\"name\":\"{code}\",\"message\":\"token=private recipient@example.test\"}}"));
        var receipt = await ActivatorUtilities.CreateInstance<IdentityEmailAdapter>(services).SendAsync("recipient@example.test", "Notice", "safe", Guid.NewGuid().ToString(), CancellationToken.None);
        receipt.Delivered.ShouldBeFalse();
        receipt.IsPermanentFailure.ShouldBe(permanent);
        receipt.ProviderReceipt.ShouldBeNull();
        receipt.ToString().ShouldNotContain("private");
    }

    [Test]
    public async Task Non_json_permanent_provider_error_is_still_permanent_and_redacted()
    {
        using var services = AdapterServices(new RecordingTransport(HttpStatusCode.Unauthorized, "private provider response"));
        var receipt = await ActivatorUtilities.CreateInstance<IdentityEmailAdapter>(services)
            .SendAsync("recipient@example.test", "Notice", "safe", Guid.NewGuid().ToString(), CancellationToken.None);
        receipt.IsPermanentFailure.ShouldBeTrue();
        receipt.ToString().ShouldNotContain("private");
    }

    [Test]
    public void From_address_drift_changes_the_retry_fingerprint()
    {
        using var services = AdapterServices(new RecordingTransport(HttpStatusCode.OK, "{}"));
        var adapter = ActivatorUtilities.CreateInstance<IdentityEmailAdapter>(services);
        var first = adapter.GetRequestFingerprint("recipient@example.test", "Confirm", "stable body");
        services.GetRequiredService<IOptions<IdentityEmailOptions>>().Value.FromAddress = "other@example.test";
        adapter.GetRequestFingerprint("recipient@example.test", "Confirm", "stable body").ShouldNotBe(first);
    }

    [Test]
    public async Task Actual_generic_worker_host_starts_and_resolves_background_dependencies()
    {
        using var databaseScope = TestServices.CreateScope();
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = "Development" });
        builder.Configuration["ConnectionStrings:CleanArchitectureDb"] = databaseScope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.GetConnectionString();
        builder.Configuration["IdentityAccess:Email:Enabled"] = "false";
        builder.Configuration["IdentityAccess:Email:PublicOrigin"] = "https://app.example.test";
        builder.AddOutboxWorkerServices();
        var sink = new TestEmailSink();
        builder.Services.AddSingleton<IIdentityEmailSender>(sink);
        using var host = builder.Build();
        await host.StartAsync();
        using var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<OutboxDispatcher>().ShouldNotBeNull();
        scope.ServiceProvider.GetRequiredService<IUser>().Id.ShouldBeNull();
        scope.ServiceProvider.GetRequiredService<MediatR.IMediator>().ShouldNotBeNull();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var identity = new CleanArchitecture.Infrastructure.Identity.ApplicationUser { Id = Guid.NewGuid(), Email = "worker-isolated@example.test" };
        context.Users.Add(identity);
        var message = CleanArchitecture.Domain.IdentityAccess.Outbox.OutboxMessage.Create("identity.invitation.signin.notice.requested", System.Text.Json.JsonSerializer.Serialize(new { IdentityId = identity.Id }), DateTimeOffset.UtcNow);
        context.OutboxMessages.Add(message);
        await context.SaveChangesAsync();
        await scope.ServiceProvider.GetRequiredService<OutboxDispatcher>().DispatchDueAsync(CancellationToken.None);
        (await context.OutboxMessages.AsNoTracking().SingleAsync(item => item.Id == message.Id)).Status.ShouldBe(CleanArchitecture.Domain.IdentityAccess.Outbox.OutboxMessageStatus.Delivered);
        sink.Sent.ShouldContain(item => item.IdempotencyKey == message.Id.ToString() && item.Recipient == identity.Email);
        await host.StopAsync();
    }

    [Test]
    public async Task Enabled_worker_rejects_missing_email_configuration_before_migration_or_claims()
    {
        using var scope = TestServices.CreateScope();
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = "Development" });
        builder.Configuration["ConnectionStrings:CleanArchitectureDb"] = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.GetConnectionString();
        builder.Configuration["IdentityAccess:Email:Enabled"] = "true";
        builder.AddOutboxWorkerServices();
        using var host = builder.Build();
        (await Should.ThrowAsync<InvalidOperationException>(() => host.StartAsync())).Message.ShouldBe("Identity email delivery is not configured.");
    }

    [TestCase("Production")]
    [TestCase("Staging")]
    [TestCase("CustomerDeployment")]
    public async Task Deployment_without_wrapping_configuration_fails_before_database_access(string environment)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = environment });
        builder.AddIdentityDataProtection();
        using var host = builder.Build();
        (await Should.ThrowAsync<InvalidOperationException>(() => host.StartAsync()))
            .Message.ShouldBe("Identity Data Protection configuration is incomplete.");
    }

    [TestCase("Development")]
    [TestCase("Test")]
    [TestCase("Testing")]
    public async Task Explicit_local_test_environments_can_start_without_wrapping_configuration(string environment)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = environment });
        builder.AddIdentityDataProtection();
        using var host = builder.Build();
        await host.StartAsync();
        await host.StopAsync();
    }

    [Test]
    public void Separate_web_and_worker_providers_share_the_explicit_application_and_encrypted_keyring()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"outbox-keyring-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        using var rsa = RSA.Create(2048);
        var certificateRequest = new CertificateRequest("CN=outbox-isolated-test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = certificateRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
        var certificatePath = Path.Combine(directory, "test.pfx");
        File.WriteAllBytes(certificatePath, certificate.Export(X509ContentType.Pfx));
        try
        {
            using var web = ProtectedProvider(directory, certificatePath);
            using var worker = ProtectedProvider(directory, certificatePath);
            const string purpose = "identity-access.registration.outbox-secret.v1";
            var ciphertext = web.GetRequiredService<IDataProtectionProvider>().CreateProtector(purpose).Protect("isolated-secret");
            worker.GetRequiredService<IDataProtectionProvider>().CreateProtector(purpose).Unprotect(ciphertext).ShouldBe("isolated-secret");
            File.ReadAllText(Directory.GetFiles(directory, "key-*.xml").Single()).ShouldContain("encryptedSecret");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static ServiceProvider ProtectedProvider(string directory, string certificatePath)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = "Production" });
        builder.Configuration["IdentityAccess:DataProtection:ApplicationName"] = "explicit-compatible-application";
        builder.Configuration["IdentityAccess:DataProtection:KeyRingPath"] = directory;
        builder.Configuration["IdentityAccess:DataProtection:CertificatePath"] = certificatePath;
        builder.AddIdentityDataProtection();
        return builder.Services.BuildServiceProvider();
    }

    private static ServiceProvider AdapterServices(HttpMessageHandler transport)
    {
        var services = new ServiceCollection();
        services.AddSingleton(new HttpClient(transport));
        var options = new IdentityEmailOptions { FromAddress = "sender@example.test", PublicOrigin = "https://app.example.test" };
        typeof(IdentityEmailOptions).GetProperty("ApiKey")?.SetValue(options, "isolated-test-key");
        services.AddSingleton(Options.Create(options));
        return services.BuildServiceProvider();
    }

    private sealed class RecordingTransport(HttpStatusCode status, string response) : HttpMessageHandler
    {
        public string? Uri { get; private set; }
        public string? Key { get; private set; }
        public string? Authorization { get; private set; }
        public string? Body { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri = request.RequestUri!.ToString();
            Key = request.Headers.GetValues("Idempotency-Key").Single();
            Authorization = request.Headers.Authorization!.ToString();
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(response) };
        }
    }
}

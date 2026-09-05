using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.DataProtection;
using System.Security.Cryptography.X509Certificates;

namespace CleanArchitecture.Infrastructure.IdentityAccess;

public static class IdentityDataProtectionConfiguration
{
    public static void AddIdentityDataProtection(this IHostApplicationBuilder builder)
    {
        var section = builder.Configuration.GetSection("IdentityAccess:DataProtection");
        var applicationName = section["ApplicationName"];
        var keyRingPath = section["KeyRingPath"];
        var certificatePath = section["CertificatePath"];
        var protection = builder.Services.AddDataProtection();
        // Preserve the existing Web discriminator when omitted. Deployments explicitly carry that exact value
        // to both processes; changing it invalidates existing envelopes and authentication cookies.
        if (!string.IsNullOrWhiteSpace(applicationName)) protection.SetApplicationName(applicationName);
        if (!string.IsNullOrWhiteSpace(keyRingPath)) protection.PersistKeysToFileSystem(new DirectoryInfo(keyRingPath));
        if (!string.IsNullOrWhiteSpace(certificatePath))
        {
            try
            {
                var certificate = X509CertificateLoader.LoadPkcs12FromFile(certificatePath, section["CertificatePassword"], X509KeyStorageFlags.EphemeralKeySet);
                if (!certificate.HasPrivateKey) throw new InvalidOperationException();
                protection.ProtectKeysWithCertificate(certificate);
            }
            catch (Exception exception) when (exception is System.Security.Cryptography.CryptographicException or IOException or UnauthorizedAccessException or InvalidOperationException)
            { throw new InvalidOperationException("Identity Data Protection certificate is unavailable."); }
        }
        var emailEnabled = bool.TryParse(builder.Configuration["IdentityAccess:Email:Enabled"], out var enabled) && enabled;
        builder.Services.AddSingleton<IHostedService>(provider => new Readiness(provider, builder.Environment, emailEnabled, applicationName, keyRingPath, certificatePath));
    }

    private sealed class Readiness(IServiceProvider services, IHostEnvironment environment, bool emailEnabled, string? applicationName, string? keyRingPath, string? certificatePath) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            if (Data.DatabaseMigrationExecutionPolicy.IsOpenApiDocumentGeneration(services.GetService<Microsoft.AspNetCore.Hosting.Server.IServer>()?.GetType().FullName))
                return Task.CompletedTask;
            var localEnvironment = environment.IsDevelopment() || environment.IsEnvironment("Test") || environment.IsEnvironment("Testing");
            if (((!localEnvironment || emailEnabled) && (string.IsNullOrWhiteSpace(applicationName) || string.IsNullOrWhiteSpace(keyRingPath))) ||
                (!localEnvironment && string.IsNullOrWhiteSpace(certificatePath)))
                throw new InvalidOperationException("Identity Data Protection configuration is incomplete.");
            return Task.CompletedTask;
        }
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

using CleanArchitecture.Application.IdentityAccess.Lifecycle;
using CleanArchitecture.Application.IdentityAccess.People;
using CleanArchitecture.Domain.IdentityAccess.People;
using CleanArchitecture.Infrastructure.IdentityAccess.People;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CleanArchitecture.Infrastructure.IdentityAccess.Lifecycle;

/// <summary>
/// The two things a deployment must not be allowed to start in the middle of (IA-REQ-055, IA-REQ-056).
/// <para>
/// Both are configurations that look like an operator's intention and are contradicted by what is beside them.
/// Neither can be answered by a route, because by the time a route answers, the process has already accepted a
/// request it should never have been available to serve — so the refusal is a refusal to start, in both the web
/// application and the worker, and it names what is missing without naming any value.
/// </para>
/// <para>
/// This is not a general configuration validator. Everything else fails closed at the point of use, which is the
/// right place for it: a deployment with no retention policy erases nothing, one that cannot seal a document
/// refuses to record one, one whose evidence is unsigned admits nothing. What is here is only what is worse than
/// refusing at the point of use, because the deployment would otherwise carry on looking healthy.
/// </para>
/// </summary>
public sealed class IdentityDeploymentGuard(
    IConfiguration configuration,
    IRetentionPolicy retention,
    IPersonalDataMode personalData,
    IOptions<IdentityDocumentProtectionOptions> documents,
    IServiceProvider services) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // The document generator builds the application without meaning to run it, and it has neither keys nor a
        // policy because it is not a deployment. The same escape the Data Protection readiness check takes.
        if (Data.DatabaseMigrationExecutionPolicy.IsOpenApiDocumentGeneration(
                services.GetService<Microsoft.AspNetCore.Hosting.Server.IServer>()?.GetType().FullName))
        {
            return Task.CompletedTask;
        }

        if (Refusal() is { } refusal) throw new InvalidOperationException(refusal);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Why this deployment may not start, or null. Separated so a test can ask without a host.</summary>
    public string? Refusal()
    {
        var recovery = configuration.GetSection("IdentityAccess:Recovery");

        // Armed and unable to verify. The adapter already refuses everything in that state, so nothing is
        // *served* wrongly — but a deployment that answers 503 to every route for want of one setting is an
        // outage an operator will spend hours on, and the process knew the answer before it opened a socket.
        if (!string.IsNullOrWhiteSpace(recovery["Deployment"]) && string.IsNullOrWhiteSpace(recovery["VerificationKey"]))
        {
            return "IdentityAccess:Recovery:Deployment names a recovering deployment, but IdentityAccess:Recovery:VerificationKey is missing. A deployment that cannot verify its evidence can admit nothing.";
        }

        if (personalData.Classification != DataClassification.Real) return null;

        // Real personal data, and nothing that would ever delete it. A deployment can legitimately run with no
        // policy — it simply erases nothing — but not while holding real people's data, which is the one case
        // where "erases nothing" is a decision somebody has to have taken rather than one nobody noticed.
        if (retention.Current is null)
        {
            return "IdentityAccess:PersonalData:Mode is Real, but no retention policy could be read from IdentityAccess:RetentionPolicy. Real personal data with no retention policy is a deployment nobody decided to run.";
        }

        // Real documents, and no key to seal them under. This one fails closed at the point of use as well, but
        // the failure lands on the person trying to register rather than on the operator who forgot the key.
        var protection = documents.Value;
        if (protection.CurrentKeyVersion < 1 || !protection.FingerprintKeys.ContainsKey(protection.CurrentKeyVersion))
        {
            return "IdentityAccess:PersonalData:Mode is Real, but IdentityAccess:People:DocumentProtection has no key material for its current version.";
        }

        return null;
    }
}

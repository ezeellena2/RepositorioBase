using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CleanArchitecture.Application.IdentityAccess.Lifecycle;
using Microsoft.Extensions.Configuration;

namespace CleanArchitecture.Infrastructure.IdentityAccess.Lifecycle;

/// <summary>
/// Reads the admission evidence and decides what this deployment may admit (IA-REQ-055).
/// <para>
/// **What arms the guard.** `IdentityAccess:Recovery:Deployment` alone. Naming a deployment is an operator
/// saying "this process may be running on restored data", and from that moment nothing else about the
/// configuration can talk it back down: a missing verification key is not a reason to admit anything, it is a
/// deployment that cannot verify and therefore stays closed. A deployment that names nothing is not recovering
/// from anything and admits what it always did.
/// </para>
/// <para>
/// That last sentence is the one place this guard can be wrong in the open direction, so it is named rather than
/// buried. The alternative — closing every deployment by default — would make an ordinary start indistinguishable
/// from a restore, and a guard nobody can start a system with is a guard somebody switches off.
/// </para>
/// <para>
/// **What can open it.** Only a signed record read from outside the database: a file this process is pointed at,
/// or a value handed to it by environment configuration. Nothing read from the restored database is consulted at
/// all, and nothing contained in a backup could be, because the key that verifies the signature is not in one.
/// </para>
/// <para>
/// **When it is decided.** Once per process, at construction, and therefore again on every start. Not cached
/// anywhere durable — the only durable place a restored deployment has is the restored database, which is exactly
/// what must not be able to open it.
/// </para>
/// </summary>
public sealed class ConfiguredRecoveryAdmission : IRecoveryAdmission
{
    public ConfiguredRecoveryAdmission(IConfiguration configuration, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(timeProvider);
        Current = Evaluate(configuration.GetSection("IdentityAccess:Recovery"), timeProvider.GetUtcNow());
    }

    public RecoveryAdmission Current { get; }

    private static RecoveryAdmission Evaluate(IConfigurationSection section, DateTimeOffset now)
    {
        var deployment = Trimmed(section["Deployment"]);

        // Nothing arms it. This deployment is not recovering, and a guard that closed it would be closing every
        // ordinary start as well.
        if (deployment is null) return RecoveryAdmission.NotRecovering;

        // Armed and unable to verify. Deliberately not "unarmed": an operator who deployed the evidence and
        // forgot the key must not thereby get a fully open deployment on restored data, which is the exact shape
        // of mistake this guard exists for. A deployment in that state now also refuses to start, in
        // IdentityDeploymentGuard — this remains the answer for anything that constructs the adapter directly.
        var key = Trimmed(section["VerificationKey"]);
        if (key is null) return RecoveryAdmission.ClosedBecause(RecoveryAdmissionReason.EvidenceInvalid);

        var payload = ReadEvidence(section);
        if (payload is null) return RecoveryAdmission.ClosedBecause(RecoveryAdmissionReason.EvidenceMissing);

        RecoveryEvidence? evidence;
        try
        {
            evidence = JsonSerializer.Deserialize<RecoveryEvidence>(payload);
        }
        catch (JsonException)
        {
            return RecoveryAdmission.ClosedBecause(RecoveryAdmissionReason.EvidenceInvalid);
        }

        if (evidence is null || string.IsNullOrWhiteSpace(evidence.Signature)) return RecoveryAdmission.ClosedBecause(RecoveryAdmissionReason.EvidenceInvalid);

        // Wrong deployment first: a record that is genuine, correctly signed and about somewhere else is the most
        // dangerous thing this could be handed, because everything about it looks right.
        if (!string.Equals(evidence.Deployment, deployment, StringComparison.Ordinal))
            return RecoveryAdmission.ClosedBecause(RecoveryAdmissionReason.EvidenceInvalid);

        if (!Verifies(evidence, key)) return RecoveryAdmission.ClosedBecause(RecoveryAdmissionReason.EvidenceInvalid);

        if (!DateTimeOffset.TryParse(evidence.ExpiresAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var expiresAt))
            return RecoveryAdmission.ClosedBecause(RecoveryAdmissionReason.EvidenceInvalid);
        if (!DateTimeOffset.TryParse(evidence.RecoveryEpoch, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var epoch))
            return RecoveryAdmission.ClosedBecause(RecoveryAdmissionReason.EvidenceInvalid);

        if (expiresAt <= now) return RecoveryAdmission.ClosedBecause(RecoveryAdmissionReason.EvidenceStale);

        // `release` is the operator saying reconciliation is done. Without it, verified evidence buys
        // authentication and revalidation and nothing else.
        return new RecoveryAdmission(
            evidence.Release ? RecoveryAdmissionState.Open : RecoveryAdmissionState.Quarantined,
            RecoveryAdmissionReason.Verified,
            epoch);
    }

    /// <summary>
    /// The evidence, from outside the database: a file this process is pointed at, or a value configuration hands
    /// it. Both are things an operator controls at deployment time and neither is in a backup.
    /// </summary>
    private static string? ReadEvidence(IConfigurationSection section)
    {
        var inline = Trimmed(section["Evidence"]);
        if (inline is not null) return inline;

        var path = Trimmed(section["EvidenceFile"]);
        if (path is null) return null;

        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            // Unreadable is missing. There is no reading of it that could be safer than not having it.
            return null;
        }
    }

    /// <summary>
    /// HMAC-SHA256 over the canonical form, with the operator's key. Nothing here needs a package: the point is
    /// that the key lives outside the restored data, not that the algorithm is exotic.
    /// </summary>
    private static bool Verifies(RecoveryEvidence evidence, string key)
    {
        Span<byte> expected = stackalloc byte[32];
        try
        {
            HMACSHA256.HashData(Convert.FromBase64String(key), Encoding.UTF8.GetBytes(Canonical(evidence)), expected);
            return CryptographicOperations.FixedTimeEquals(expected, Convert.FromBase64String(evidence.Signature!));
        }
        catch (FormatException)
        {
            // A key or a signature that is not Base64 verifies nothing.
            return false;
        }
    }

    /// <summary>
    /// What is signed. Every field except the signature, in a fixed order with a separator that cannot appear in
    /// any of them — so no two different records can produce the same string to sign.
    /// </summary>
    public static string Canonical(RecoveryEvidence evidence) => string.Join(
        '\n',
        evidence.Deployment,
        evidence.RecoveryEpoch,
        evidence.BackupId,
        evidence.IssuedAt,
        evidence.ExpiresAt,
        evidence.Release ? "release" : "quarantine");

    private static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>
/// The operator-controlled record C6 describes. Every field is a string on purpose: what arrives is a document
/// somebody else produced, and parsing it is this deployment's job rather than the deserializer's.
/// </summary>
public sealed record RecoveryEvidence(
    string? Deployment,
    string? RecoveryEpoch,
    string? BackupId,
    string? IssuedAt,
    string? ExpiresAt,
    bool Release,
    string? Signature);

namespace CleanArchitecture.Application.IdentityAccess.Platform;

/// <summary>What a Platform enrollment is handed once, and never again: the shared key and the codes.</summary>
public sealed record PlatformMfaSecret(string EncryptedSecret, string SharedKey, string ProvisioningUri);

/// <summary>One recovery code as its owner sees it, and as the database stores it.</summary>
public sealed record PlatformRecoveryCodeIssue(string Code, string Hash);

/// <summary>
/// The second factor itself. It is a port because the secret is encrypted with the deployment's key material and
/// verified against a clock, neither of which the application layer may reach directly.
/// </summary>
public interface IPlatformMfaVerifier
{
    /// <summary>Mints a secret, already encrypted, together with what the owner needs to enroll an authenticator.</summary>
    PlatformMfaSecret Create(string accountName);

    /// <summary>Whether a submitted code belongs to this encrypted secret at this moment.</summary>
    bool Verify(string encryptedSecret, string code, DateTimeOffset now);
}

/// <summary>Mints one-time recovery codes and hashes them, so only the hash is ever stored.</summary>
public interface IPlatformRecoveryCodeFactory
{
    IReadOnlyList<PlatformRecoveryCodeIssue> Create();

    string Hash(string code);
}

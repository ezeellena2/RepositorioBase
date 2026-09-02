using System.Security.Cryptography;
using System.Text;

namespace CleanArchitecture.Web.Infrastructure.Identity;

/// <summary>
/// Pure key derivation for the login transport partitions (IA-REQ-019). Keys are opaque SHA-256 digests in
/// separate domains, so neither the account nor the client address can be recovered from a partition key,
/// and every spelling of one account lands in the same partition.
/// </summary>
public static class LoginRateLimitPartitioner
{
    /// <summary>Named rate-limiting policy that only <c>POST /api/identity/sessions</c> carries.</summary>
    public const string PolicyName = "identity-login";

    private const string AccountDomain = "account:";
    private const string ClientDomain = "ip:";
    private const string SentinelDomain = "sentinel:";

    /// <summary>Mirrors the session command normalization (trim + lower-invariant); null when there is no account.</summary>
    public static string? NormalizeEmail(string? email) =>
        string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();

    public static string AccountKey(string email)
    {
        var normalized = NormalizeEmail(email) ?? throw new ArgumentException("An account partition requires an email.", nameof(email));
        return Digest(AccountDomain, normalized);
    }

    public static string ClientKey(string clientAddress)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientAddress);
        return Digest(ClientDomain, clientAddress);
    }

    /// <summary>Shared fail-closed partition for requests that cannot be attributed to an account but may still reach the endpoint.</summary>
    public static string SentinelKey(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return Digest(SentinelDomain, reason);
    }

    private static string Digest(string domain, string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(domain + value)));
}

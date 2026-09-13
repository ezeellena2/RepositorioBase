using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace CleanArchitecture.AppHost;

/// <summary>
/// Fills in the local development settings the application refuses to start without, once, into this project's user
/// secrets — a file outside the repository.
/// <para>
/// It exists because those settings are not decisions a developer should have to make: a key ring has to persist
/// somewhere, mail has to land somewhere readable, and a documentary fingerprint needs a key that has no default by
/// design. Writing them by hand is a step everybody repeats and somebody eventually gets wrong.
/// </para>
/// <para>
/// Two rules make this safe to run on every start. It never overwrites a value that is already configured, because
/// a rotated key ring path or a replaced fingerprint key would silently orphan data already protected under the old
/// one. And it never runs outside an interactive Development run, so nothing about a deployment is decided here.
/// </para>
/// </summary>
internal static class LocalDevelopmentSetup
{
    private const string ApplicationNameKey = "IdentityAccess:DataProtection:ApplicationName";
    private const string KeyRingPathKey = "IdentityAccess:DataProtection:KeyRingPath";
    private const string EmailEnabledKey = "IdentityAccess:Email:Enabled";
    private const string EmailFromAddressKey = "IdentityAccess:Email:FromAddress";
    private const string EmailDropPathKey = "IdentityAccess:Email:LocalDropPath";
    private const string EmailProviderKey = "IdentityAccess:Email:Provider";
    private const string FingerprintVersionKey = "IdentityAccess:People:DocumentProtection:CurrentKeyVersion";
    private const string FingerprintKeyKey = "IdentityAccess:People:DocumentProtection:FingerprintKeys:1";

    /// <summary>The user-secrets identifier declared in AppHost.csproj. The identifier is not a secret; the file it names is.</summary>
    private const string SecretsId = "identity-access-apphost-local";

    /// <summary>
    /// A harness that starts this host sets this to <c>false</c>. A test run must not write to the machine's
    /// development settings: it would be a suite reaching into the state normal operation depends on.
    /// </summary>
    private const string EnabledKey = "IdentityAccess:LocalSetup:Enabled";

    internal static void EnsureConfigured(IDistributedApplicationBuilder builder)
    {
        if (!builder.ExecutionContext.IsRunMode || !builder.Environment.IsDevelopment()) return;
        if (builder.Configuration.GetValue(EnabledKey, true) is false) return;

        var home = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var root = Path.Combine(string.IsNullOrWhiteSpace(home) ? Path.GetTempPath() : home, "identity-access-local");

        var defaults = new Dictionary<string, Func<string>>(StringComparer.Ordinal)
        {
            [ApplicationNameKey] = () => "identity-access-local",
            [KeyRingPathKey] = () => Path.Combine(root, "keyring"),
            [EmailEnabledKey] = () => "true",
            [EmailFromAddressKey] = () => "identity-access@example.test",
            [EmailDropPathKey] = () => Path.Combine(root, "mail"),
            [FingerprintVersionKey] = () => "1",

            // 32 bytes, generated once and kept. Regenerating it on every start would make every documentary
            // identity recorded under the previous key unfindable, which looks exactly like the data being lost.
            [FingerprintKeyKey] = () => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        };

        // A developer who named a real provider asked for real delivery. Filling the drop path in behind them would
        // contradict that choice, and the application refuses the combination, so the default is not offered.
        var provider = builder.Configuration[EmailProviderKey];
        var deliversToFolder = string.IsNullOrWhiteSpace(provider) ||
            string.Equals(provider.Trim(), "LocalFolder", StringComparison.OrdinalIgnoreCase);
        if (!deliversToFolder) defaults.Remove(EmailDropPathKey);

        var added = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, produce) in defaults)
        {
            if (!string.IsNullOrWhiteSpace(builder.Configuration[key])) continue;
            var value = produce();
            added[key] = value;

            // Also set it in memory: the secrets file was read when the builder was created, so a value written
            // now would otherwise only take effect on the next run.
            builder.Configuration[key] = value;
        }

        Directory.CreateDirectory(builder.Configuration[KeyRingPathKey]!);
        if (deliversToFolder) Directory.CreateDirectory(builder.Configuration[EmailDropPathKey]!);

        if (added.Count == 0) return;
        Persist(added);
    }

    /// <summary>
    /// Merges the added keys into the user-secrets file, leaving everything already in it alone. The layout is the
    /// flat one `dotnet user-secrets set` writes, so the two remain interchangeable.
    /// </summary>
    private static void Persist(IReadOnlyDictionary<string, string> added)
    {
        var path = SecretsPath();
        if (path is null) return;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var existing = File.Exists(path)
                ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new JsonObject()
                : new JsonObject();

            foreach (var (key, value) in added)
            {
                if (existing.ContainsKey(key)) continue;
                existing[key] = value;
            }

            File.WriteAllText(path, existing.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or JsonException)
        {
            // The run still works: everything was set in memory above. Only persistence across runs is lost, and
            // saying so is more useful than refusing to start a development environment over a file permission.
            Console.WriteLine($"Local development settings could not be saved to user secrets ({failure.GetType().Name}). They apply to this run only.");
        }
    }

    private static string? SecretsPath()
    {
        var appData = Environment.GetEnvironmentVariable("APPDATA");
        if (!string.IsNullOrWhiteSpace(appData))
        {
            return Path.Combine(appData, "Microsoft", "UserSecrets", SecretsId, "secrets.json");
        }

        var home = Environment.GetEnvironmentVariable("HOME");
        return string.IsNullOrWhiteSpace(home)
            ? null
            : Path.Combine(home, ".microsoft", "usersecrets", SecretsId, "secrets.json");
    }
}

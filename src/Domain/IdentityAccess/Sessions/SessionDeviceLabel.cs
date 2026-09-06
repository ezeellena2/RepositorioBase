namespace CleanArchitecture.Domain.IdentityAccess.Sessions;

/// <summary>
/// The only descriptions of a device this system keeps. The list is closed and server-derived: a raw `User-Agent`
/// is a fingerprint, and a client-supplied label is a place to write whatever the client likes into somebody's
/// security screen (IA-REQ-029).
/// </summary>
public static class SessionDeviceLabel
{
    public const string Other = "Other";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "Windows",
        "macOS",
        "Linux",
        "Android",
        "iPhone",
        "iPad",
        Other
    };

    /// <summary>Anything not on the list becomes <see cref="Other"/> rather than being refused: a session is not worth losing over a label.</summary>
    public static string Normalize(string? value) => value is not null && All.Contains(value) ? value : Other;
}

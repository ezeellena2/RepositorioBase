namespace CleanArchitecture.Application.IdentityAccess.ExternalLogins;

/// <summary>
/// The provider names this system knows. They are the authentication scheme name and the stored link's provider
/// column at once, so a route value is normalized to one of these before anything reads or writes with it — a
/// link recorded as "google" and one recorded as "Google" would otherwise be two different links.
/// </summary>
public static class ExternalProviders
{
    public const string Google = "Google";

    public static string? Canonical(string? provider) =>
        string.Equals(provider, Google, StringComparison.OrdinalIgnoreCase) ? Google : null;
}

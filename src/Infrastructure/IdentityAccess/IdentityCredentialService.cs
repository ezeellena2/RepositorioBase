using CleanArchitecture.Application.IdentityAccess.Credentials;
using CleanArchitecture.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;

namespace CleanArchitecture.Infrastructure.IdentityAccess;

/// <summary>
/// Replaces a password through ASP.NET Identity's own hasher and validators, so the configured `PasswordOptions`
/// are the only policy there is and no second copy of it can drift.
/// </summary>
public sealed class IdentityCredentialService(UserManager<ApplicationUser> userManager) : IIdentityCredentialService
{
    public async Task<CredentialWriteResult> ReplacePasswordAsync(Guid identityId, string newPassword, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(identityId.ToString());
        if (user is null) return CredentialWriteResult.Refused(Field("newPassword", "The password could not be replaced."));

        foreach (var validator in userManager.PasswordValidators)
        {
            var validated = await validator.ValidateAsync(userManager, user, newPassword);
            if (!validated.Succeeded) return CredentialWriteResult.Refused(Describe(validated));
        }

        // The hash is set directly rather than through RemovePassword/AddPassword, which is two writes and leaves
        // a window in which the account has no password at all.
        user.PasswordHash = userManager.PasswordHasher.HashPassword(user, newPassword);
        var updated = await userManager.UpdateAsync(user);
        return updated.Succeeded ? CredentialWriteResult.Applied() : CredentialWriteResult.Refused(Describe(updated));
    }

    public async Task<bool> HasPasswordAsync(Guid identityId, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(identityId.ToString());
        return user is not null && await userManager.HasPasswordAsync(user);
    }

    /// <summary>
    /// Field-indexed, and describing the policy rather than the value: a message that echoed what was typed would
    /// put a candidate password into a response body (IA-REQ-029).
    /// </summary>
    private static IReadOnlyDictionary<string, string[]> Describe(IdentityResult result) =>
        Field("newPassword", result.Errors.Select(error => error.Description).ToArray());

    private static IReadOnlyDictionary<string, string[]> Field(string name, params string[] messages) =>
        new Dictionary<string, string[]> { [name] = messages };
}

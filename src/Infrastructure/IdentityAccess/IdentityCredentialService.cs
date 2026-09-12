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
        if (user is null) return CredentialWriteResult.Concurrency();

        var passwordErrors = new List<IdentityError>();
        var passwordRejected = false;
        foreach (var validator in userManager.PasswordValidators)
        {
            var validated = await validator.ValidateAsync(userManager, user, newPassword);
            passwordRejected |= !validated.Succeeded;
            passwordErrors.AddRange(validated.Errors);
        }

        if (passwordRejected || passwordErrors.Count > 0)
            return CredentialWriteResult.PasswordPolicy(
                IdentityPasswordValidationDetails.From(passwordErrors, userManager.Options.Password));

        // The hash is set directly rather than through RemovePassword/AddPassword, which is two writes and leaves
        // a window in which the account has no password at all.
        user.PasswordHash = userManager.PasswordHasher.HashPassword(user, newPassword);
        var updated = await userManager.UpdateAsync(user);
        if (updated.Succeeded) return CredentialWriteResult.Applied();
        if (updated.Errors.Any(error => string.Equals(error.Code, nameof(IdentityErrorDescriber.ConcurrencyFailure), StringComparison.Ordinal)))
            return CredentialWriteResult.Concurrency();
        throw new InvalidOperationException("Identity did not persist the password update.");
    }

    public async Task<bool> HasPasswordAsync(Guid identityId, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(identityId.ToString());
        return user is not null && await userManager.HasPasswordAsync(user);
    }

}

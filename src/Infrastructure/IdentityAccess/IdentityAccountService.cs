using CleanArchitecture.Application.IdentityAccess.Common;
using CleanArchitecture.Application.IdentityAccess.Organizations;
using CleanArchitecture.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;

namespace CleanArchitecture.Infrastructure.IdentityAccess;

public sealed class IdentityAccountService(
    UserManager<ApplicationUser> userManager,
    IEnumerable<IUserValidator<ApplicationUser>> userValidators,
    IEnumerable<IPasswordValidator<ApplicationUser>> passwordValidators) : IIdentityAccountService
{
    public async Task<IdentityAccount?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(normalizedEmail);
        return user is null ? null : new IdentityAccount(user.Id, user.Email!, user.EmailConfirmed);
    }

    public async Task<IdentityAccountValidationResult> ValidatePendingRegistrationAsync(string normalizedEmail, string password, CancellationToken cancellationToken)
    {
        var user = NewPendingUser(normalizedEmail);
        foreach (var validator in userValidators)
        {
            if (!(await validator.ValidateAsync(userManager, user)).Succeeded) return new IdentityAccountValidationResult(false);
        }

        foreach (var validator in passwordValidators)
        {
            if (!(await validator.ValidateAsync(userManager, user, password)).Succeeded) return new IdentityAccountValidationResult(false);
        }

        return new IdentityAccountValidationResult(true);
    }

    public async Task<IdentityAccountCreationResult> CreatePendingAsync(string normalizedEmail, string password, CancellationToken cancellationToken)
    {
        var user = NewPendingUser(normalizedEmail);
        var result = await userManager.CreateAsync(user, password);
        return result.Succeeded
            ? new IdentityAccountCreationResult(new IdentityAccount(user.Id, user.Email!, false), false)
            : new IdentityAccountCreationResult(null, true);
    }

    public async Task ActivateAsync(Guid identityId, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(identityId.ToString()) ?? throw new InvalidOperationException("identity_not_found");
        if (user.EmailConfirmed) return;
        user.EmailConfirmed = true;
        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded) throw new InvalidOperationException("identity_activation_failed");
    }

    private static ApplicationUser NewPendingUser(string normalizedEmail) =>
        new() { UserName = normalizedEmail, Email = normalizedEmail, EmailConfirmed = false };
}

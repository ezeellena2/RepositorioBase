using CleanArchitecture.Application.IdentityAccess.ExternalLogins;
using CleanArchitecture.Infrastructure.Data;
using CleanArchitecture.Infrastructure.Data.Configurations.IdentityAccess;
using CleanArchitecture.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Infrastructure.IdentityAccess;

/// <summary>
/// Provider links over ASP.NET Identity's own <c>AspNetUserLogins</c>.
/// <para>
/// Nothing here keeps a second registry of provider accounts. The store's primary key is already
/// (provider, subject), so "one provider account belongs to one identity" is enforced by the database rather than
/// by a check this code could race, and the ordinary <see cref="UserManager{T}"/> writes participate in whatever
/// transaction the caller opened, because the store shares the request's <c>DbContext</c>.
/// </para>
/// <para>
/// The display name column carries the address the provider asserted at the time of linking. It is what the
/// account screen shows; the subject identifier never leaves this class.
/// </para>
/// </summary>
public sealed class ExternalIdentityService(UserManager<ApplicationUser> userManager, ApplicationDbContext context) : IExternalIdentityService
{
    public async Task<Guid?> FindIdentityBySubjectAsync(string provider, string subject, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(subject)) return null;
        var user = await userManager.FindByLoginAsync(provider, subject);
        return user?.Id;
    }

    /// <summary>
    /// Read straight from the store rather than through <see cref="UserManager{T}"/>, because the handle and the
    /// linking timestamp are columns the framework's own <c>UserLoginInfo</c> has no room for.
    /// </summary>
    public async Task<IReadOnlyList<ExternalLinkView>> ListAsync(Guid identityId, CancellationToken cancellationToken) =>
        await context.UserLogins
            .AsNoTracking()
            .Where(login => login.UserId == identityId)
            .OrderBy(login => login.LoginProvider)
            .Select(login => new ExternalLinkView(
                EF.Property<string>(login, ExternalLinkColumns.Handle),
                login.LoginProvider,
                login.ProviderDisplayName ?? string.Empty,
                EF.Property<DateTimeOffset>(login, ExternalLinkColumns.LinkedAt)))
            .ToListAsync(cancellationToken);

    public async Task<bool> HasLinkAsync(Guid identityId, string provider, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(identityId.ToString());
        if (user is null) return false;
        var logins = await userManager.GetLoginsAsync(user);
        return logins.Any(login => string.Equals(login.LoginProvider, provider, StringComparison.Ordinal));
    }

    public async Task<bool> LinkAsync(Guid identityId, string provider, string subject, string providerEmail, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(identityId.ToString());
        if (user is null) return false;

        // A refusal here is the unique key doing its job: the subject is already somebody's. It is answered as a
        // conflict rather than thrown, because that is a state the caller is expected to meet.
        var added = await userManager.AddLoginAsync(user, new UserLoginInfo(provider, subject, providerEmail));
        return added.Succeeded;
    }

    public async Task<bool> UnlinkAsync(Guid identityId, string provider, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(identityId.ToString());
        if (user is null) return false;
        var logins = await userManager.GetLoginsAsync(user);
        var login = logins.FirstOrDefault(candidate => string.Equals(candidate.LoginProvider, provider, StringComparison.Ordinal));
        if (login is null) return false;
        var removed = await userManager.RemoveLoginAsync(user, login.LoginProvider, login.ProviderKey);
        return removed.Succeeded;
    }

    public async Task<int> AuthenticatorCountAsync(Guid identityId, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(identityId.ToString());
        if (user is null) return 0;
        var password = await userManager.HasPasswordAsync(user) ? 1 : 0;
        var logins = await userManager.GetLoginsAsync(user);
        return password + logins.Count;
    }

    public async Task<Guid?> CreateFromProviderAsync(string normalizedEmail, CancellationToken cancellationToken)
    {
        // Confirmed on creation, and only because the provider asserted a verified address; the caller refuses
        // the assertion otherwise. No password is set: the provider link is this identity's only authenticator
        // until the person adds one, which is what makes the last-authenticator rule matter.
        var user = new ApplicationUser { UserName = normalizedEmail, Email = normalizedEmail, EmailConfirmed = true };
        var created = await userManager.CreateAsync(user);
        return created.Succeeded ? user.Id : null;
    }
}

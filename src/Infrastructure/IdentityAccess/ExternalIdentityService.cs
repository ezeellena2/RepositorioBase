using CleanArchitecture.Application.IdentityAccess.ExternalLogins;
using CleanArchitecture.Domain.IdentityAccess.Identities;
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
/// (provider, subject), so "one provider account belongs to one identity" is a database rule rather than one this
/// code could drift from, and the ordinary <see cref="UserManager{T}"/> writes participate in whatever
/// transaction the caller opened, because the store shares the request's <c>DbContext</c>.
/// </para>
/// <para>
/// That database rule is the backstop and not the decision. PostgreSQL refuses a duplicate by raising, and the
/// transaction is aborted from that moment — too late to record a refusal. So callers take
/// <c>IExternalSubjectLock</c> and decide by reading, and nothing here is expected to meet the unique key.
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

        // This is the last line of defence, not the first. A duplicate subject is refused by the database, and
        // the database refuses it by throwing inside a transaction it then aborts — which is why the callers
        // take `IExternalSubjectLock` first and decide by reading. What a false here reports is a validator
        // saying no, which is a state the caller is expected to meet.
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

    public async Task<Guid?> CreateFromProviderAsync(
        string normalizedEmail,
        string preferredLanguage,
        CancellationToken cancellationToken)
    {
        // Confirmed on creation, and only because the provider asserted a verified address; the caller refuses
        // the assertion otherwise. That is the same fact the state records, so it is set here rather than left to
        // a later confirmation that has nothing to confirm (IA-REQ-054). No password is set: the provider link is
        // this identity's only authenticator until the person adds one, which is what makes the
        // last-authenticator rule matter.
        var user = new ApplicationUser
        {
            UserName = normalizedEmail,
            Email = normalizedEmail,
            EmailConfirmed = true,
            Status = IdentityAccountStatus.Active,
            PreferredLanguage = preferredLanguage
        };
        var created = await userManager.CreateAsync(user);
        return created.Succeeded ? user.Id : null;
    }
}

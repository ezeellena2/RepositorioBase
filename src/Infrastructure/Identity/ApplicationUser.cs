using CleanArchitecture.Domain.IdentityAccess.Identities;
using Microsoft.AspNetCore.Identity;

namespace CleanArchitecture.Infrastructure.Identity;

public class ApplicationUser : IdentityUser<Guid>
{
    /// <summary>
    /// What this account is allowed to be (IA-REQ-054). It is a real column rather than something derived from
    /// <see cref="IdentityUser{TKey}.EmailConfirmed"/> and the lockout window, because a derived answer can only
    /// express the reasons somebody already thought of: parking an account and suspending one are neither an
    /// unconfirmed address nor a lockout, and there is nowhere to put them.
    /// <para>
    /// The migration that adds it maps every existing row onto the answer that row already gave — confirmed to
    /// <see cref="IdentityAccountStatus.Active"/>, unconfirmed to
    /// <see cref="IdentityAccountStatus.PendingConfirmation"/> — so no account's meaning changes on the day it
    /// arrives.
    /// </para>
    /// </summary>
    public IdentityAccountStatus Status { get; set; } = IdentityAccountStatus.PendingConfirmation;
}

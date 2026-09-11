using CleanArchitecture.Domain.IdentityAccess.Identities;
using Microsoft.AspNetCore.Identity;

namespace CleanArchitecture.Infrastructure.Identity;

public class ApplicationUser : IdentityUser<Guid>
{
    /// <summary>
    /// The person's explicit supported-language choice. A null value means the person has never chosen a
    /// language; delivery may then use an immutable invitation or registration snapshot before the configured
    /// fallback. Existing accounts deliberately remain null after the Phase 4 migration (IA-REQ-059).
    /// </summary>
    public string? PreferredLanguage { get; set; }

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

    /// <summary>
    /// Where this account was when a Platform operator stopped it, and <see langword="null"/> whenever it is not
    /// stopped. Lifting a suspension restores this rather than assuming <see cref="IdentityAccountStatus.Active"/>:
    /// somebody who had parked their own account before an operator suspended it goes back to their own decision,
    /// which is not the operator's to undo (IA-REQ-054).
    /// </summary>
    public IdentityAccountStatus? StatusBeforeSuspension { get; set; }
}

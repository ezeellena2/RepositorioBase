using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.Common.Security;
using CleanArchitecture.Application.IdentityAccess.Authorization;

namespace CleanArchitecture.Application.IdentityAccess.Lifecycle;

/// <summary>
/// Parking your own account (IA-REQ-054). It carries no subject: the account is the caller's, resolved from the
/// validated session, so there is no arbitrary-identity disable route here for anything to be pointed at.
/// </summary>
[Authorize(Permissions.IdentityAccountManage, false)]
public sealed record DeactivateAccountCommand : IRequest<Result>;

/// <summary>
/// "I would like my account back." Public because somebody whose account is parked cannot sign in to ask, and
/// neutral for the same reason recovery is: answering differently for an address with an account would make this
/// the enumeration route the rest of the system is careful not to be (IA-REQ-029).
/// </summary>
public sealed record RequestAccountReactivationCommand(string Email) : IRequest<Result>, IPublicRequest, ISensitiveRequest;

/// <summary>
/// Spending the mailed ticket. Public for the same reason, and sensitive because it carries both a usable ticket
/// and a password.
/// <para>
/// There is no `providerProofToken` here, and there will not be one: withdrawal E1 established that a provider
/// round trip demonstrates a live session at the provider, not a person. An identity that only ever signed in
/// through a provider sets a password first — through the recovery route that already exists, gated on the same
/// mailbox this ticket is sent to — and comes back with it.
/// </para>
/// </summary>
public sealed record ReactivateAccountCommand(string ReactivationToken, string Password) : IRequest<Result>, IPublicRequest, ISensitiveRequest;

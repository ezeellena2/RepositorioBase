using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.Common.Security;

namespace CleanArchitecture.Application.IdentityAccess.Invitations.RegisterInvitedUser;

/// <summary>
/// The invitee has no account and therefore no session, so this request is public by declaration rather than by
/// omission. It never accepts the invitation: confirmation and sign-in still stand between it and acceptance.
/// </summary>
public sealed record RegisterInvitedUserCommand(string Token, string Password)
    : IRequest<Result>, IPublicRequest, ISensitiveRequest;

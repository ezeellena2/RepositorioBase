using CleanArchitecture.Application.Common.Models;

namespace CleanArchitecture.Application.IdentityAccess.Invitations.RegisterInvitedUser;

public sealed class RegisterInvitedUserCommandHandler : IRequestHandler<RegisterInvitedUserCommand, Result>
{
    public Task<Result> Handle(RegisterInvitedUserCommand request, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}

using CleanArchitecture.Application.Common.Models;

namespace CleanArchitecture.Application.IdentityAccess.Invitations.ResendInvitation;

public sealed class ResendInvitationCommandHandler : IRequestHandler<ResendInvitationCommand, Result>
{
    public Task<Result> Handle(ResendInvitationCommand request, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}

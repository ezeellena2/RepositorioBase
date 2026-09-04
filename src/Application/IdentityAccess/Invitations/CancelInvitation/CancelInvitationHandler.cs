using CleanArchitecture.Application.Common.Models;

namespace CleanArchitecture.Application.IdentityAccess.Invitations.CancelInvitation;

public sealed class CancelInvitationCommandHandler : IRequestHandler<CancelInvitationCommand, Result>
{
    public Task<Result> Handle(CancelInvitationCommand request, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}

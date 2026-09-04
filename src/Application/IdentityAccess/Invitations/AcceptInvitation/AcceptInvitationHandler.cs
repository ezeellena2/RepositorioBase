using CleanArchitecture.Application.Common.Models;

namespace CleanArchitecture.Application.IdentityAccess.Invitations.AcceptInvitation;

public sealed class AcceptInvitationCommandHandler : IRequestHandler<AcceptInvitationCommand, Result<AcceptedInvitation>>
{
    public Task<Result<AcceptedInvitation>> Handle(AcceptInvitationCommand request, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}

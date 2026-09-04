using CleanArchitecture.Application.Common.Models;

namespace CleanArchitecture.Application.IdentityAccess.Invitations.InviteMember;

public sealed class InviteMemberCommandHandler : IRequestHandler<InviteMemberCommand, Result<IssuedInvitation>>
{
    public Task<Result<IssuedInvitation>> Handle(InviteMemberCommand request, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}

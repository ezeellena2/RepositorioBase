using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.Common.Security;
using FluentValidation;

namespace CleanArchitecture.Application.IdentityAccess.Platform.Invitations;

/// <summary>
/// Consumes both halves of the onboarding proof: the invitation token, which says which Platform offer is being
/// answered, and the confirmation token, which says the address received it. It confirms the identity and creates
/// no membership (IA-REQ-041).
/// </summary>
public sealed record ConfirmPlatformInviteeCommand(string Token, string ConfirmationToken)
    : IRequest<Result>, IPublicRequest, ISensitiveRequest;

public sealed class ConfirmPlatformInviteeCommandValidator : AbstractValidator<ConfirmPlatformInviteeCommand>
{
    public ConfirmPlatformInviteeCommandValidator()
    {
        RuleFor(command => command.Token).NotEmpty().MaximumLength(256);
        RuleFor(command => command.ConfirmationToken).NotEmpty().MaximumLength(256);
    }
}

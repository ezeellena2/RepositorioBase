using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.Common.Security;
using CleanArchitecture.Application.Common.Validation;
using FluentValidation;

namespace CleanArchitecture.Application.IdentityAccess.Platform.Invitations;

/// <summary>
/// Consumes the confirmation token, which says the address received the mail, and confirms the identity. It
/// creates no membership (IA-REQ-041).
/// <para>
/// It deliberately does not also require the invitation token. An earlier revision did, on the reasoning that
/// two proofs are better than one — but the confirmation token is single-use, sealed, delivered only to the
/// recipient's address, and its envelope names exactly one invitation and one identity, so the offer being
/// answered is already pinned. Anyone able to read the confirmation token can read the invitation link in the
/// same mailbox, so the second token added no defence against any attacker while making a one-click
/// confirmation link impossible: nothing in a mail could carry a token the system only ever stored as a hash.
/// This is the same proof an organization confirmation requires.
/// </para>
/// </summary>
public sealed record ConfirmPlatformInviteeCommand(string ConfirmationToken)
    : IRequest<Result>, IPublicRequest, ISensitiveRequest;

public sealed class ConfirmPlatformInviteeCommandValidator : AbstractValidator<ConfirmPlatformInviteeCommand>
{
    public ConfirmPlatformInviteeCommandValidator() =>
        RuleFor(command => command.ConfirmationToken)
            .NotEmpty().WithErrorCode(ValidationErrorCodes.Required).WithMessage("A confirmation token is required.")
            .MaximumLength(256).WithErrorCode(ValidationErrorCodes.TooLong).WithMessage("The confirmation token must be 256 characters or fewer.")
            .OverridePropertyName("confirmationToken");
}

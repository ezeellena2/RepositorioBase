using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.Common.Security;
using CleanArchitecture.Application.Common.Validation;
using CleanArchitecture.Application.IdentityAccess.Authorization;
using CleanArchitecture.Domain.IdentityAccess.Invitations;
using CleanArchitecture.Domain.IdentityAccess.Tenants;

namespace CleanArchitecture.Application.IdentityAccess.Invitations.InviteMember;

/// <summary>
/// The tenant is compared against the validated session's active tenant; it never establishes context.
/// </summary>
[Authorize(Permissions.MembersInvite, true)]
public sealed record InviteMemberCommand(TenantId TenantId, string Email, IReadOnlyList<Guid> RoleIds)
    : IRequest<Result<IssuedInvitation>>;

public sealed class InviteMemberCommandValidator : AbstractValidator<InviteMemberCommand>
{
    public InviteMemberCommandValidator()
    {
        RuleFor(command => command.Email)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithErrorCode(ValidationErrorCodes.Required).WithMessage("Enter an email address.")
            .MaximumLength(256).WithErrorCode(ValidationErrorCodes.TooLong).WithMessage("The email address must be 256 characters or fewer.")
            .Must(HasSupportedEmailShape).WithErrorCode(ValidationErrorCodes.EmailFormat).WithMessage("Enter an email address.")
            .OverridePropertyName("email");

        RuleFor(command => command.RoleIds)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithErrorCode(ValidationErrorCodes.Required).WithMessage("Choose at least one role.")
            .Must(roleIds => roleIds is not null && roleIds.All(roleId => roleId != Guid.Empty))
            .WithErrorCode(ValidationErrorCodes.RoleIdsInvalid)
            .WithMessage("Choose valid roles.")
            .OverridePropertyName("roleIds");
    }

    private static bool HasSupportedEmailShape(string email)
    {
        try
        {
            _ = Invitation.Canonicalize(email);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}

/// <summary>
/// Deliberately carries no token. The usable credential is minted for the recipient and reaches them through the
/// encrypted outbox envelope; handing it back to whoever issued the invitation would let anyone holding
/// members.invite register an account for an address they do not control (IA-REQ-015/018).
/// </summary>
public sealed record IssuedInvitation(Guid InvitationId, DateTimeOffset ExpiresAt);

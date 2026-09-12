using System.Globalization;
using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.Common.Security;
using CleanArchitecture.Application.IdentityAccess.Authorization;

namespace CleanArchitecture.Application.IdentityAccess.People.Profile;

/// <summary>
/// A person's own profile. Neither request carries a subject: they resolve the caller's own row and there is no
/// route that resolves anybody else's (IA-REQ-050).
/// </summary>
[Authorize(Permissions.IdentityProfileRead, false)]
public sealed record GetPersonalProfileQuery : IRequest<Result<PersonalProfileResponse>>;

[Authorize(Permissions.IdentityProfileManage, false)]
public sealed record UpdatePersonalProfileCommand(string FullName, string DisplayName, string Version)
    : IRequest<Result<PersonalProfileResponse>>;

public sealed class UpdatePersonalProfileCommandValidator : AbstractValidator<UpdatePersonalProfileCommand>
{
    public UpdatePersonalProfileCommandValidator()
    {
        RuleFor(command => command.FullName)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("A full name is required.")
            .MaximumLength(200).WithMessage("The full name must be 200 characters or fewer.")
            .OverridePropertyName("fullName");

        RuleFor(command => command.DisplayName)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("A display name is required.")
            .MaximumLength(60).WithMessage("The display name must be 60 characters or fewer.")
            .OverridePropertyName("displayName");

        RuleFor(command => command.Version)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("A profile version is required.")
            .Must(IsCanonicalUnsignedDecimal).WithMessage("The profile version must be an unsigned decimal token.")
            .OverridePropertyName("version");
    }

    private static bool IsCanonicalUnsignedDecimal(string value) =>
        uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) &&
        string.Equals(value, parsed.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
}

public sealed record PersonalProfileResponse(
    string FullName,
    string DisplayName,
    string Email,
    Guid PersonalTenantId,
    PersonalDocumentResponse? Document,
    string Version,
    DateTimeOffset UpdatedAt);

/// <summary>
/// What the owner is shown about their own document: that it exists, and its last two digits. The number, the
/// ciphertext, the fingerprint, the key version and the digit count are all absent by construction.
/// </summary>
public sealed record PersonalDocumentResponse(
    string Country,
    string Type,
    string Status,
    string MaskedNumber,
    bool CorrectionAvailable);

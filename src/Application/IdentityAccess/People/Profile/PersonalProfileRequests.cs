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

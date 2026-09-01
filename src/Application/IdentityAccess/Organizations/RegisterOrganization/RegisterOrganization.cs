using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.Common.Security;
using CleanArchitecture.Domain.IdentityAccess.Organizations;

namespace CleanArchitecture.Application.IdentityAccess.Organizations.RegisterOrganization;

public sealed record RegisterOrganizationCommand(
    string Email,
    string Password,
    string LegalName,
    string Cuit) : IRequest<Result>, IPublicRequest, ISensitiveRequest;

public interface IRegistrationIdempotencyStore
{
    Task<RegistrationSubmissionClaim> TryClaimAsync(string canonicalKey, CancellationToken cancellationToken);

    Task CoordinateBusinessIntentAsync(string normalizedEmail, string normalizedCuit, CancellationToken cancellationToken);
}

public sealed record RegistrationSubmissionClaim(RegistrationSubmission Submission, bool IsOwner);

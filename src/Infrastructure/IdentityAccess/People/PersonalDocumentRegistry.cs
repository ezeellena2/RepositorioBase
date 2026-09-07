using CleanArchitecture.Application.IdentityAccess.People;
using CleanArchitecture.Domain.IdentityAccess.People;
using CleanArchitecture.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Infrastructure.IdentityAccess.People;

/// <summary>
/// The two answers a claim needs, and no surface beyond them. It takes fingerprints and returns a boolean: there is
/// no member here that could tell a caller whose document a fingerprint belongs to.
/// </summary>
public sealed class PersonalDocumentRegistry(ApplicationDbContext context) : IPersonalDocumentRegistry
{
    public async Task<bool> IsRecordedAsync(IReadOnlyList<DocumentFingerprintValue> fingerprints, CancellationToken cancellationToken)
    {
        if (fingerprints.Count == 0) return false;
        var values = fingerprints.Select(fingerprint => fingerprint.Value).ToArray();
        return await context.IdentityDocumentFingerprints
            .AsNoTracking()
            .AnyAsync(candidate => values.Contains(candidate.Fingerprint), cancellationToken);
    }

    public async Task<bool> IsRecordedByAnotherAsync(
        Guid identityId, IReadOnlyList<DocumentFingerprintValue> fingerprints, CancellationToken cancellationToken)
    {
        if (fingerprints.Count == 0) return false;
        var values = fingerprints.Select(fingerprint => fingerprint.Value).ToArray();
        return await context.IdentityDocumentFingerprints
            .AsNoTracking()
            .AnyAsync(candidate => values.Contains(candidate.Fingerprint) && candidate.IdentityId != identityId, cancellationToken);
    }

    public Task<bool> OwnsPersonalContextAsync(Guid identityId, CancellationToken cancellationToken) =>
        context.PersonalTenantOwnerships.AsNoTracking().AnyAsync(ownership => ownership.IdentityId == identityId, cancellationToken);
}

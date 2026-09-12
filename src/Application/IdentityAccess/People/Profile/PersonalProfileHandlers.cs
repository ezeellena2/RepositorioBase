using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.IdentityAccess.Common;
using CleanArchitecture.Application.IdentityAccess.Organizations;
using CleanArchitecture.Application.IdentityAccess.Sessions;
using CleanArchitecture.Domain.IdentityAccess.People;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Application.IdentityAccess.People.Profile;

public sealed class GetPersonalProfileQueryHandler(
    IApplicationDbContext context,
    IIdentityAccountService identities,
    IIdentityDocumentProtector documents,
    IPersonalProfileStamps stamps,
    ICurrentSession currentSession) : IRequestHandler<GetPersonalProfileQuery, Result<PersonalProfileResponse>>
{
    public async Task<Result<PersonalProfileResponse>> Handle(GetPersonalProfileQuery request, CancellationToken cancellationToken)
    {
        if (currentSession.IsInvalid || currentSession.IdentityId is null)
            return Result<PersonalProfileResponse>.Failure(IdentityAccessErrors.InvalidSession());

        var identityId = currentSession.IdentityId.Value;
        var profile = await context.PersonProfiles.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.IdentityId == identityId, cancellationToken);
        if (profile is null) return Result<PersonalProfileResponse>.Failure(IdentityAccessErrors.PersonalProfileNotFound());

        var identity = await identities.FindByIdAsync(identityId, cancellationToken);
        var stamp = await stamps.ReadAsync(identityId, cancellationToken);
        if (identity is null || stamp is null) return Result<PersonalProfileResponse>.Failure(IdentityAccessErrors.InvalidSession());

        var document = await context.IdentityDocuments.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.IdentityId == identityId, cancellationToken);
        var disputeOpen = await context.IdentityDocumentDisputes
            .AnyAsync(dispute => dispute.SubjectIdentityId == identityId && dispute.Status == DocumentDisputeStatus.Open, cancellationToken);
        return Result<PersonalProfileResponse>.Success(
            PersonalProfileProjection.From(profile, identity.Email, document, documents, stamp.Value, disputeOpen));
    }
}

public sealed class UpdatePersonalProfileCommandHandler(
    IApplicationTransaction transaction,
    IApplicationDbContext context,
    IIdentityAccountService identities,
    IIdentityDocumentProtector documents,
    IPersonalProfileStamps stamps,
    ICurrentSession currentSession) : IRequestHandler<UpdatePersonalProfileCommand, Result<PersonalProfileResponse>>
{
    public async Task<Result<PersonalProfileResponse>> Handle(UpdatePersonalProfileCommand request, CancellationToken cancellationToken)
    {
        if (currentSession.IsInvalid || currentSession.IdentityId is null)
            return Result<PersonalProfileResponse>.Failure(IdentityAccessErrors.InvalidSession());

        var identityId = currentSession.IdentityId.Value;
        return await transaction.ExecuteAsync(async ct =>
        {
            var profile = await context.PersonProfiles.SingleOrDefaultAsync(candidate => candidate.IdentityId == identityId, ct);
            if (profile is null) return Result<PersonalProfileResponse>.Failure(IdentityAccessErrors.PersonalProfileNotFound());

            // The echoed version is the row's own concurrency token, so an edit made against a value somebody else
            // already replaced loses rather than overwriting it. There is no merge: the whole edit applies or none
            // of it does (IA-REQ-035).
            var before = await stamps.ReadAsync(identityId, ct);
            if (before is null || !string.Equals(before.Value.Version, request.Version, StringComparison.Ordinal))
                return Result<PersonalProfileResponse>.Failure(IdentityAccessErrors.PersonalProfileConcurrencyConflict());

            profile.Rename(request.FullName!.Trim(), request.DisplayName!.Trim());
            try
            {
                await context.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Result<PersonalProfileResponse>.Failure(IdentityAccessErrors.PersonalProfileConcurrencyConflict());
            }

            var identity = await identities.FindByIdAsync(identityId, ct);
            var after = await stamps.ReadAsync(identityId, ct);
            if (identity is null || after is null) return Result<PersonalProfileResponse>.Failure(IdentityAccessErrors.InvalidSession());
            var document = await context.IdentityDocuments.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.IdentityId == identityId, ct);
            var disputeOpen = await context.IdentityDocumentDisputes
                .AnyAsync(dispute => dispute.SubjectIdentityId == identityId && dispute.Status == DocumentDisputeStatus.Open, ct);
            return Result<PersonalProfileResponse>.Success(
                PersonalProfileProjection.From(profile, identity.Email, document, documents, after.Value, disputeOpen));
        }, cancellationToken);
    }
}

/// <summary>What the owner is shown about themselves, and only that.</summary>
internal static class PersonalProfileProjection
{
    internal static PersonalProfileResponse From(
        PersonProfile profile,
        string email,
        IdentityDocument? document,
        IIdentityDocumentProtector documents,
        PersonalProfileStamp stamp,
        bool disputeOpen) =>
        new(
            profile.FullName,
            profile.DisplayName,
            email,
            profile.PersonalTenantId.Value,
            Document(document, documents, disputeOpen),
            stamp.Version,
            stamp.UpdatedAt);

    private static PersonalDocumentResponse? Document(IdentityDocument? document, IIdentityDocumentProtector documents, bool disputeOpen)
    {
        if (document is null) return null;
        if (document.PurgedAt is not null)
        {
            // A purged document has nothing to correct. Offering a dispute over one would be offering to
            // resurrect a row an erasure deliberately emptied.
            return new PersonalDocumentResponse(document.Country.ToString(), document.DocumentType.ToString(), "purged", string.Empty, false);
        }

        // The owner's own read is one of the protector's two named seams. What leaves this method is the mask, not
        // the number: the last two digits, and one bullet for every digit before them.
        var revealed = documents.Reveal(document.Ciphertext);
        var masked = revealed is { } value
            ? new string('•', Math.Max(0, value.Number.Length - 2)) + (value.Number.Length <= 2 ? value.Number : value.Number[^2..])
            : string.Empty;

        // The owner's own data, saying nothing about anyone else: a dispute is available unless this person
        // already has one open, because at most one is open at a time (IA-REQ-058).
        return new PersonalDocumentResponse(
            document.Country.ToString(),
            document.DocumentType.ToString(),
            revealed is null ? "unreadable" : "recorded",
            masked,
            !disputeOpen);
    }
}

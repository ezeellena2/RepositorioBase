using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.IdentityAccess.Common;
using CleanArchitecture.Application.IdentityAccess.Organizations.RegisterOrganization;
using CleanArchitecture.Application.IdentityAccess.Security;
using CleanArchitecture.Application.IdentityAccess.Sessions;
using CleanArchitecture.Domain.IdentityAccess.People;

namespace CleanArchitecture.Application.IdentityAccess.People.CreatePersonalContext;

/// <summary>
/// The proved half, for somebody whose session already establishes who they are. It spends the claim budget before
/// it looks at anything, takes the business lock, and then either writes the whole graph or refuses with the one
/// code that covers every reason it could not (SPEC section 14.3).
/// </summary>
public sealed class CreatePersonalContextCommandHandler(
    IApplicationTransaction transaction,
    IApplicationDbContext context,
    IRegistrationIdempotencyStore idempotencyStore,
    IPersonalDocumentRegistry registry,
    IIdentityDocumentProtector documents,
    IIdentityDocumentFingerprint fingerprints,
    ISharedAttemptBudget budget,
    IPersonalDataMode mode,
    ICurrentSession currentSession,
    TimeProvider timeProvider) : IRequestHandler<CreatePersonalContextCommand, Result>
{
    public async Task<Result> Handle(CreatePersonalContextCommand request, CancellationToken cancellationToken)
    {
        if (currentSession.IsInvalid || currentSession.IdentityId is null)
            return Result.Failure(IdentityAccessErrors.InvalidSession());

        var identityId = currentSession.IdentityId.Value;
        var fullName = request.FullName!.Trim();
        var displayName = request.DisplayName!.Trim();
        var document = NormalizedDocument.From(
            IdentityDocumentCountry.AR,
            IdentityDocumentKind.DNI,
            request.DocumentNumber!);

        // Spent before the claim is attempted, and outside the business transaction, so a refused claim still costs
        // an attempt. Counting only successful claims would leave the enumeration this budget exists to bound.
        var decision = await budget.SpendAsync(PersonalAttemptBudgets.DocumentClaim, identityId.ToString("N"), cancellationToken);
        if (PersonalContextFactory.Refusal(decision) is { } refusal) return Result.Failure(refusal);

        var values = fingerprints.ForRetainedKeys(document);
        var ciphertext = documents.Protect(document);

        return await transaction.ExecuteAsync(async ct =>
        {
            // Two claims on one documentary identity are serialized here rather than racing the unique index, so
            // the loser is told the same thing as somebody who simply already owns a context.
            await idempotencyStore.CoordinateBusinessIntentAsync(identityId.ToString("N"), values[0].Value, ct);

            if (await registry.OwnsPersonalContextAsync(identityId, ct)) return Result.Failure(IdentityAccessErrors.PersonalRegistrationConflict());
            if (await registry.IsRecordedAsync(values, ct)) return Result.Failure(IdentityAccessErrors.PersonalRegistrationConflict());

            PersonalContextFactory.Add(
                context,
                identityId,
                fullName,
                displayName,
                ciphertext,
                values,
                mode.Classification,
                $"personal-context-{identityId:N}",
                timeProvider.GetUtcNow());

            await context.SaveChangesAsync(ct);
            return Result.Success();
        }, cancellationToken);
    }

}

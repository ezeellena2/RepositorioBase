using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.IdentityAccess.Authorization;
using CleanArchitecture.Application.IdentityAccess.Common;
using CleanArchitecture.Application.IdentityAccess.Credentials;
using CleanArchitecture.Application.IdentityAccess.Platform;
using CleanArchitecture.Application.IdentityAccess.Platform.Administrators;
using CleanArchitecture.Application.IdentityAccess.Sessions;
using CleanArchitecture.Domain.IdentityAccess.Auditing;
using CleanArchitecture.Domain.IdentityAccess.Memberships;
using CleanArchitecture.Domain.IdentityAccess.People;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Application.IdentityAccess.People.Documents;

/// <summary>
/// Opening a dispute over your own recorded document (IA-REQ-058).
/// <para>
/// The claimed tuple is normalized and protected before anything else happens, so the number exists in this
/// handler for exactly as long as it takes to encrypt it and never reaches a field, a log or a record. What is
/// stored is ciphertext; what is returned is an opaque identifier.
/// </para>
/// </summary>
public sealed class OpenDocumentDisputeCommandHandler(
    IApplicationTransaction transaction,
    IApplicationDbContext context,
    IIdentityDocumentProtector protector,
    IRecentIdentityProofStore proofs,
    ICurrentSession session,
    TimeProvider timeProvider) : IRequestHandler<OpenDocumentDisputeCommand, Result<OpenedDocumentDispute>>
{
    public async Task<Result<OpenedDocumentDispute>> Handle(OpenDocumentDisputeCommand request, CancellationToken cancellationToken)
    {
        if (session.IsInvalid || session.IdentityId is not { } identityId || session.SessionId is not { } sessionId)
            return Result<OpenedDocumentDispute>.Failure(IdentityAccessErrors.InvalidSession());

        if (!IdentityDocumentDispute.IsAcceptedCode(request.ReasonCode))
            return Result<OpenedDocumentDispute>.Failure(IdentityAccessErrors.InvalidDocumentDispute());

        NormalizedDocument claimed;
        try
        {
            claimed = NormalizedDocument.From(
                Enum.Parse<IdentityDocumentCountry>(request.ClaimedCountry, ignoreCase: false),
                Enum.Parse<IdentityDocumentKind>(request.ClaimedType, ignoreCase: false),
                request.ClaimedNumber);
        }
        catch (Exception failure) when (failure is ArgumentException or FormatException or OverflowException)
        {
            // Deliberately without the value. The one thing in scope here is a document number.
            return Result<OpenedDocumentDispute>.Failure(IdentityAccessErrors.InvalidDocumentDispute());
        }

        return await transaction.ExecuteAsync(async ct =>
        {
            // The proof is spent before anything is read, so an unproved caller learns nothing about whether
            // this identity records a document at all.
            if (!await proofs.TryConsumeAsync(identityId, sessionId, ProofActions.DocumentDispute, ct))
                return Result<OpenedDocumentDispute>.Failure(IdentityAccessErrors.RecentProofRequired());

            var recorded = await context.IdentityDocuments
                .AnyAsync(document => document.IdentityId == identityId && document.PurgedAt == null, ct);
            if (!recorded) return Result<OpenedDocumentDispute>.Failure(IdentityAccessErrors.PersonalProfileNotFound());

            var standing = await context.IdentityDocumentDisputes
                .AnyAsync(dispute => dispute.SubjectIdentityId == identityId && dispute.Status == DocumentDisputeStatus.Open, ct);
            if (standing) return Result<OpenedDocumentDispute>.Failure(IdentityAccessErrors.DocumentDisputeConflict());

            var now = timeProvider.GetUtcNow();
            var opened = IdentityDocumentDispute.Open(identityId, request.ReasonCode, protector.Protect(claimed), now);
            context.IdentityDocumentDisputes.Add(opened);

            // The audit says a dispute was opened and why the person said it was. It cannot say what they claimed,
            // because `AuditEvent` admits only stable identifiers and a document number is not one.
            context.AuditEvents.Add(AuditEvent.CreateSessionEvent(
                identityId, sessionId.Value, "identity.document.dispute.opened", AuditCorrelation.Current(), "opened", opened.ReasonCode));
            await context.SaveChangesAsync(ct);
            return Result<OpenedDocumentDispute>.Success(new OpenedDocumentDispute(opened.DisputeId));
        }, cancellationToken);
    }
}

/// <summary>
/// The operator's half (IA-REQ-058). A `corrected` outcome replaces the ciphertext and every retained fingerprint
/// row in one transaction, under the same uniqueness that refuses a duplicate at creation.
/// </summary>
public sealed class ResolveDocumentDisputeCommandHandler(
    IApplicationTransaction transaction,
    IApplicationDbContext context,
    IIdentityDocumentProtector protector,
    IIdentityDocumentFingerprint fingerprints,
    IPersonalDocumentRegistry registry,
    ICurrentTenant currentTenant,
    ICurrentSession session,
    IRecentMfaVerifier recentMfa,
    TimeProvider timeProvider) : IRequestHandler<ResolveDocumentDisputeCommand, Result>
{
    public async Task<Result> Handle(ResolveDocumentDisputeCommand request, CancellationToken cancellationToken)
    {
        if (!await recentMfa.HasRecentStepUpAsync(cancellationToken)) return Result.Failure(IdentityAccessErrors.RecentMfaRequired());
        if (!IdentityDocumentDispute.IsAcceptedCode(request.EvidenceReference)) return Result.Failure(IdentityAccessErrors.InvalidDocumentDispute());
        if (request.Outcome is not ("corrected" or "rejected")) return Result.Failure(IdentityAccessErrors.InvalidDocumentDispute());

        return await transaction.ExecuteAsync(async ct =>
        {
            var platform = await PlatformContext.ResolveAsync(context, currentTenant, ct);
            if (platform is null || session.IdentityId is not { } actorId) return Result.Failure(IdentityAccessErrors.InvalidPlatformOperation());

            // Asked before anything is read about the dispute. An operator who is both parties is the bypass this
            // route exists to make impossible, and being told so early is part of that.
            if (actorId == request.SubjectIdentityId) return Result.Failure(IdentityAccessErrors.SelfResolutionRefused());

            var dispute = await context.IdentityDocumentDisputes.SingleOrDefaultAsync(
                candidate => candidate.DisputeId == request.DisputeId
                    && candidate.SubjectIdentityId == request.SubjectIdentityId
                    && candidate.Status == DocumentDisputeStatus.Open,
                ct);

            // No stored dispute means no route that writes a document. That absence is the contract, so an
            // unknown or already-settled dispute answers the same as one that never existed.
            if (dispute is null) return Result.Failure(IdentityAccessErrors.DocumentDisputeNotFound());

            var membership = await context.TenantMemberships.SingleOrDefaultAsync(
                candidate => candidate.TenantId == platform.Id && candidate.IdentityId == actorId && candidate.Status == MembershipStatus.Active, ct);
            if (membership is null) return Result.Failure(IdentityAccessErrors.InvalidPlatformOperation());

            var now = timeProvider.GetUtcNow();
            if (request.Outcome == "rejected")
            {
                dispute.Settle(DocumentDisputeStatus.Rejected, now);
                Audit(platform.Id, actorId, dispute, "rejected");
                await context.SaveChangesAsync(ct);
                return Result.Success();
            }

            var document = await context.IdentityDocuments
                .Include(candidate => candidate.Fingerprints)
                .SingleOrDefaultAsync(candidate => candidate.IdentityId == request.SubjectIdentityId && candidate.PurgedAt == null, ct);
            if (document is null) return Result.Failure(IdentityAccessErrors.PersonalProfileNotFound());

            // The claimed value is opened only here, only to key it, and only for as long as that takes. A
            // deployment whose key cannot open the envelope has a dispute nobody can resolve, which is a state to
            // report rather than a value to guess at.
            var claimed = protector.Reveal(dispute.ClaimedCiphertext);
            if (claimed is null) return Result.Failure(IdentityAccessErrors.InvalidDocumentDispute());

            var keyed = fingerprints.ForRetainedKeys(claimed.Value);

            // The same uniqueness that refuses a duplicate at creation. That the number belongs to somebody else
            // is a fact an audited, MFA-proved operator may be told — and that no self-service caller ever is.
            if (await registry.IsRecordedByAnotherAsync(request.SubjectIdentityId, keyed, ct))
                return Result.Failure(IdentityAccessErrors.DocumentAlreadyRecorded());

            var previousKeyVersions = document.Correct(protector.Protect(claimed.Value), keyed, now);
            dispute.Settle(DocumentDisputeStatus.Corrected, now);
            context.IdentityDocumentCorrectionRecords.Add(IdentityDocumentCorrectionRecord.Of(
                request.SubjectIdentityId, dispute.DisputeId, membership.Id.Value, request.EvidenceReference, previousKeyVersions, now));
            Audit(platform.Id, actorId, dispute, "corrected");

            // One SaveChanges inside one transaction: the ciphertext, every fingerprint row, the settled dispute
            // and its record commit together or not at all.
            await context.SaveChangesAsync(ct);
            return Result.Success();
        }, cancellationToken);
    }

    private void Audit(Domain.IdentityAccess.Tenants.TenantId platformId, Guid actorId, IdentityDocumentDispute dispute, string outcome) =>
        context.AuditEvents.Add(AuditEvent.Create(
            platformId,
            actorId,
            "identity.document.dispute.resolved",
            $"document-dispute-{dispute.DisputeId:N}",
            new Dictionary<string, string>
            {
                ["code"] = "identity.document.dispute.resolved",
                ["outcome"] = outcome,
                ["reason"] = dispute.ReasonCode
            }));
}

using System.Xml;
using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.Common.Security;
using CleanArchitecture.Application.IdentityAccess.Authorization;
using CleanArchitecture.Application.IdentityAccess.Common;
using CleanArchitecture.Application.IdentityAccess.Lifecycle;
using CleanArchitecture.Application.IdentityAccess.Organizations;
using CleanArchitecture.Application.IdentityAccess.People;
using CleanArchitecture.Application.IdentityAccess.Platform.Administrators;
using CleanArchitecture.Application.IdentityAccess.Sessions;
using CleanArchitecture.Domain.IdentityAccess.Auditing;
using CleanArchitecture.Domain.IdentityAccess.Memberships;
using CleanArchitecture.Domain.IdentityAccess.Retention;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Application.IdentityAccess.Platform.Retention;

/// <summary>What the deployment's retention policy says, as an operator reads it.</summary>
public sealed record RetentionCategoryView(string Category, string RetentionPeriod, string Trigger, string Action, bool EvidenceRequired);

public sealed record RetentionPolicyView(
    string? PolicyId,
    string? Version,
    string? Owner,
    DateOnly? ApprovedOn,
    string? Source,
    string PersonalDataMode,
    int ActiveHoldCount,
    IReadOnlyList<RetentionCategoryView> Categories);

/// <summary>What one placed hold looks like. It carries nothing about the subject except which identity it names.</summary>
public sealed record RetentionHoldView(
    Guid HoldId,
    Guid SubjectIdentityId,
    string ReasonCode,
    string Reference,
    DateTimeOffset PlacedAt,
    Guid PlacedByMembershipId,
    DateTimeOffset? ReleasedAt,
    int Version);

/// <summary>
/// Reading the policy this deployment runs under (IA-REQ-056). It is a read, so it needs the second factor to
/// have been proved in this session — the bar every operational directory meets — rather than proved recently.
/// </summary>
[Authorize(Permissions.PlatformRetentionRead, true)]
public sealed record GetRetentionPolicyQuery : IRequest<Result<RetentionPolicyView>>;

/// <summary>
/// Placing a hold. It changes no account state, no session, no permission and nothing about the subject's ability
/// to sign in: it stops their rows being erased and does nothing else (amendment A4).
/// </summary>
[Authorize(Permissions.PlatformRetentionManage, true)]
public sealed record PlaceRetentionHoldCommand(Guid SubjectIdentityId, string ReasonCode, string Reference) : IRequest<Result<RetentionHoldView>>;

/// <summary>
/// Releasing one. Idempotent, and deliberately silent about whether the hold existed: an operator who repeats a
/// release and one who names a hold that never existed get the same answer, because "does this hold exist" is not
/// a question this route is for.
/// </summary>
[Authorize(Permissions.PlatformRetentionManage, true)]
public sealed record ReleaseRetentionHoldCommand(Guid HoldId) : IRequest<Result>;

public sealed class GetRetentionPolicyQueryHandler(
    IApplicationDbContext context,
    IRetentionPolicy policy,
    IPersonalDataMode personalDataMode,
    IPlatformMfaSessionProof proof) : IRequestHandler<GetRetentionPolicyQuery, Result<RetentionPolicyView>>
{
    public async Task<Result<RetentionPolicyView>> Handle(GetRetentionPolicyQuery request, CancellationToken cancellationToken)
    {
        if (!await proof.HasProvedFactorAsync(cancellationToken))
            return Result<RetentionPolicyView>.Failure(IdentityAccessErrors.RecentMfaRequired());

        var holds = await context.RetentionLegalHolds.CountAsync(hold => hold.ReleasedAt == null, cancellationToken);
        var configured = policy.Current;

        // A deployment with no policy says so. Every field is null and the category list is empty, which is the
        // honest description of "this system will not delete anything" (IA-REQ-056).
        if (configured is null)
        {
            return Result<RetentionPolicyView>.Success(new RetentionPolicyView(
                null, null, null, null, null, personalDataMode.Classification.ToString(), holds, []));
        }

        return Result<RetentionPolicyView>.Success(new RetentionPolicyView(
            configured.PolicyId,
            configured.Version,
            configured.Owner,
            configured.ApprovedOn,
            configured.Source,
            personalDataMode.Classification.ToString(),
            holds,
            configured.Categories.Select(rule => new RetentionCategoryView(
                rule.Category.ToString(),
                XmlConvert.ToString(rule.RetentionPeriod),
                rule.Trigger.ToString(),
                rule.Action.ToString(),
                rule.EvidenceRequired)).ToArray()));
    }
}

public sealed class PlaceRetentionHoldCommandHandler(
    IApplicationTransaction transaction,
    IApplicationDbContext context,
    ICurrentTenant currentTenant,
    ICurrentSession session,
    IRecentMfaVerifier recentMfa,
    IIdentityAccountService identities,
    TimeProvider timeProvider,
    IRetentionSubjectLock subjectLock) : IRequestHandler<PlaceRetentionHoldCommand, Result<RetentionHoldView>>
{
    public async Task<Result<RetentionHoldView>> Handle(PlaceRetentionHoldCommand request, CancellationToken cancellationToken)
    {
        if (!await recentMfa.HasRecentStepUpAsync(cancellationToken))
            return Result<RetentionHoldView>.Failure(IdentityAccessErrors.RecentMfaRequired());
        if (!RetentionLegalHold.IsAcceptedReference(request.ReasonCode) || !RetentionLegalHold.IsAcceptedReference(request.Reference))
            return Result<RetentionHoldView>.Failure(IdentityAccessErrors.InvalidPlatformOperation());

        return await transaction.ExecuteAsync(async ct =>
        {
            var platform = await PlatformContext.ResolveAsync(context, currentTenant, ct);
            if (platform is null || session.IdentityId is not { } actorId)
                return Result<RetentionHoldView>.Failure(IdentityAccessErrors.InvalidPlatformOperation());

            if (await identities.FindByIdAsync(request.SubjectIdentityId, ct) is null)
                return Result<RetentionHoldView>.Failure(IdentityAccessErrors.IdentityNotFound());

            // The authority came from operating Platform, so what is recorded is the membership that carried it
            // rather than the person — the membership is the thing that says so at a point in time.
            var membership = await context.TenantMemberships.SingleOrDefaultAsync(
                candidate => candidate.TenantId == platform.Id && candidate.IdentityId == actorId && candidate.Status == MembershipStatus.Active, ct);
            if (membership is null) return Result<RetentionHoldView>.Failure(IdentityAccessErrors.InvalidPlatformOperation());

            // The same row lock the executor takes, and taken before anything about this subject is read. It is
            // what decides a hold racing a purge: whichever reaches these rows first wins, and the loser is told
            // which of the two happened (IA-REQ-056).
            await subjectLock.LockAsync([request.SubjectIdentityId], ct);

            // Asked under the lock, never before it. A purge that committed first leaves a tombstone, and a hold
            // over a tombstone would be a hold made retroactive — which C7 says cannot happen.
            if (await context.IdentityDocuments.AnyAsync(
                    document => document.IdentityId == request.SubjectIdentityId && document.PurgedAt != null, ct))
            {
                return Result<RetentionHoldView>.Failure(IdentityAccessErrors.RetentionHoldSubjectPurged());
            }

            // Read under the transaction rather than trusted from an earlier page. The partial unique index is
            // what actually decides it; this is the answer the caller can act on rather than an exception.
            var standing = await context.RetentionLegalHolds.AnyAsync(
                hold => hold.SubjectIdentityId == request.SubjectIdentityId
                    && hold.ReasonCode == request.ReasonCode
                    && hold.ReleasedAt == null,
                ct);
            if (standing) return Result<RetentionHoldView>.Failure(IdentityAccessErrors.RetentionHoldConflict());

            var now = timeProvider.GetUtcNow();
            var placed = RetentionLegalHold.Place(request.SubjectIdentityId, request.ReasonCode, request.Reference, membership.Id.Value, now);
            context.RetentionLegalHolds.Add(placed);
            context.AuditEvents.Add(AuditEvent.Create(
                platform.Id,
                actorId,
                "personal.data.hold.placed",
                $"retention-hold-{placed.HoldId:N}",
                new Dictionary<string, string>
                {
                    ["code"] = "personal.data.hold.placed",
                    ["outcome"] = "placed",
                    ["reason"] = placed.ReasonCode
                }));

            await context.SaveChangesAsync(ct);
            return Result<RetentionHoldView>.Success(Describe(placed));
        }, cancellationToken);
    }

    internal static RetentionHoldView Describe(RetentionLegalHold hold) => new(
        hold.HoldId, hold.SubjectIdentityId, hold.ReasonCode, hold.Reference,
        hold.PlacedAt, hold.PlacedByMembershipId, hold.ReleasedAt, hold.Version);
}

public sealed class ReleaseRetentionHoldCommandHandler(
    IApplicationTransaction transaction,
    IApplicationDbContext context,
    ICurrentTenant currentTenant,
    ICurrentSession session,
    IRecentMfaVerifier recentMfa,
    TimeProvider timeProvider) : IRequestHandler<ReleaseRetentionHoldCommand, Result>
{
    public async Task<Result> Handle(ReleaseRetentionHoldCommand request, CancellationToken cancellationToken)
    {
        if (!await recentMfa.HasRecentStepUpAsync(cancellationToken)) return Result.Failure(IdentityAccessErrors.RecentMfaRequired());

        return await transaction.ExecuteAsync(async ct =>
        {
            var platform = await PlatformContext.ResolveAsync(context, currentTenant, ct);
            if (platform is null || session.IdentityId is not { } actorId)
                return Result.Failure(IdentityAccessErrors.InvalidPlatformOperation());

            var hold = await context.RetentionLegalHolds.SingleOrDefaultAsync(candidate => candidate.HoldId == request.HoldId, ct);

            // An unknown hold and an already-released one both answer success, and neither writes anything. The
            // caller asked for a state — released — and that is the state.
            if (hold is null || !hold.Release(timeProvider.GetUtcNow())) return Result.Success();

            context.AuditEvents.Add(AuditEvent.Create(
                platform.Id,
                actorId,
                "personal.data.hold.released",
                $"retention-hold-{hold.HoldId:N}",
                new Dictionary<string, string>
                {
                    ["code"] = "personal.data.hold.released",
                    ["outcome"] = "released",
                    ["reason"] = hold.ReasonCode
                }));

            await context.SaveChangesAsync(ct);
            return Result.Success();
        }, cancellationToken);
    }
}

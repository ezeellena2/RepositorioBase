using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.IdentityAccess.Common;
using CleanArchitecture.Application.IdentityAccess.Organizations;
using CleanArchitecture.Application.IdentityAccess.Sessions;
using CleanArchitecture.Domain.IdentityAccess.Auditing;
using CleanArchitecture.Domain.IdentityAccess.Memberships;
using CleanArchitecture.Domain.IdentityAccess.Organizations;
using CleanArchitecture.Domain.IdentityAccess.Outbox;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Application.IdentityAccess.Organizations.RegisterOrganization;

public sealed class RegisterOrganizationCommandHandler(
    IApplicationTransaction transaction,
    IApplicationDbContext context,
    IRegistrationIdempotencyStore idempotencyStore,
    IIdentityAccountService identities,
    IValidatedOptionalSession session,
    ISecureTokenGenerator tokens,
    ITokenHasher tokenHasher,
    IOutboxSecretWriter secretWriter,
    IRegistrationInitialRoleProvisioner initialRoles,
    TimeProvider timeProvider) : IRequestHandler<RegisterOrganizationCommand, Result>
{
    public async Task<Result> Handle(RegisterOrganizationCommand request, CancellationToken cancellationToken)
    {
        if (!TryNormalize(request, out var intent)) return Result.Failure(IdentityAccessErrors.InvalidRegistration());
        if (session.IsInvalid || session.IdentityId.HasValue != (session.Email is not null)) return Result.Failure(IdentityAccessErrors.InvalidSession());
        if (session.Email is not null && !string.Equals(session.Email.Trim(), intent.Email, StringComparison.OrdinalIgnoreCase))
            return Result.Failure(IdentityAccessErrors.InvalidRegistration());

        if (session.IdentityId is null && await identities.FindByEmailAsync(intent.Email, cancellationToken) is null)
        {
            var validation = await identities.ValidatePendingRegistrationAsync(intent.Email, request.Password, cancellationToken);
            if (!validation.IsValid) return Result.Failure(IdentityAccessErrors.InvalidRegistration());
        }

        try
        {
            return await transaction.ExecuteAsync(async ct =>
            {
                var scope = session.IdentityId?.ToString("N") ?? "anonymous";
                var canonicalKey = CanonicalKey(scope, intent);
                var claim = await idempotencyStore.TryClaimAsync(canonicalKey, ct);
                if (!claim.IsOwner) return Replay(claim.Submission);

                var submission = claim.Submission;
                await idempotencyStore.CoordinateBusinessIntentAsync(intent.Email, intent.Cuit.Value, ct);
                var identity = session.IdentityId is { } identityId
                    ? new IdentityAccount(identityId, intent.Email, true)
                    : await identities.FindByEmailAsync(intent.Email, ct);

                if (session.IdentityId is null && identity is not null) return await CompleteSubmissionAsync(submission, RegistrationSubmissionOutcome.Accepted, ct);
                if (await context.OrganizationProfiles.AnyAsync(profile => profile.Cuit == intent.Cuit, ct)) return await CompleteSubmissionAsync(submission, RegistrationSubmissionOutcome.RegistrationConflict, ct);

                if (identity is null)
                {
                    var creation = await identities.CreatePendingAsync(intent.Email, request.Password, ct);
                    if (creation.IsValidationFailure || creation.Account is null) throw new ExpectedIdentityValidationFailureException();
                    identity = creation.Account;
                }

                var tenant = Tenant.CreateOrganization(TenantSlug.From($"org-{intent.Cuit.Value}"));
                var membership = TenantMembership.CreateResponsible(tenant, identity.Id);
                initialRoles.AssignResponsibleOwner(tenant, membership);
                var organization = OrganizationProfile.Create(tenant, intent.LegalName, intent.Cuit);
                var rawToken = tokens.Generate();
                var now = timeProvider.GetUtcNow();
                var outbox = OutboxMessage.Create("identity.confirmation.requested", JsonSerializer.Serialize(new ConfirmationEnvelope(identity.Id, tenant.Id.Value, membership.Id.Value)), now);
                var secret = OutboxSecret.Create(outbox.Id, tokenHasher.Hash(rawToken), secretWriter.Encrypt(rawToken), now.AddHours(24));

                context.Tenants.Add(tenant);
                context.OrganizationProfiles.Add(organization);
                context.TenantMemberships.Add(membership);
                context.OutboxMessages.Add(outbox);
                context.OutboxSecrets.Add(secret);
                context.AuditEvents.Add(AuditEvent.Create(tenant.Id, identity.Id, "organization.registration.requested", Correlation(canonicalKey), new Dictionary<string, string> { ["code"] = "organization.registration.requested", ["outcome"] = "pending_confirmation" }));
                await context.SaveChangesAsync(ct);
                return await CompleteSubmissionAsync(submission, RegistrationSubmissionOutcome.Accepted, ct);
            }, cancellationToken);
        }
        catch (ExpectedIdentityValidationFailureException)
        {
            return Result.Failure(IdentityAccessErrors.InvalidRegistration());
        }
    }

    private async Task<Result> CompleteSubmissionAsync(RegistrationSubmission submission, RegistrationSubmissionOutcome outcome, CancellationToken cancellationToken)
    {
        context.RegistrationSubmissions.Attach(submission);
        submission.Complete(outcome, timeProvider.GetUtcNow());
        await context.SaveChangesAsync(cancellationToken);
        return ResultFor(outcome);
    }

    private static Result Replay(RegistrationSubmission submission) => submission.Outcome is { } outcome
        ? ResultFor(outcome)
        : Result.Failure(IdentityAccessErrors.RegistrationConflict());

    private static Result ResultFor(RegistrationSubmissionOutcome outcome) => outcome switch
    {
        RegistrationSubmissionOutcome.Accepted => Result.Success(),
        RegistrationSubmissionOutcome.RegistrationConflict => Result.Failure(IdentityAccessErrors.RegistrationConflict()),
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Unsupported registration outcome.")
    };

    private static bool TryNormalize(RegisterOrganizationCommand request, out RegistrationIntent intent)
    {
        intent = default;
        try
        {
            if (request.Email is null || request.Password is null || request.LegalName is null || request.Cuit is null) return false;
            if (request.Email.Length > 256 || request.Password.Length > 256 || request.LegalName.Length > 256 || request.Cuit.Length > 32) return false;
            var email = request.Email.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(email) || !email.Contains('@') || string.IsNullOrWhiteSpace(request.Password)) return false;
            intent = new RegistrationIntent(email, request.LegalName.Trim(), NormalizedCuit.From(request.Cuit));
            return !string.IsNullOrWhiteSpace(intent.LegalName);
        }
        catch (ArgumentException) { return false; }
    }

    private static string CanonicalKey(string scope, RegistrationIntent intent) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{scope}|{intent.Email}|{intent.LegalName.ToUpperInvariant()}|{intent.Cuit.Value}")));
    private static string Correlation(string canonicalKey) => $"registration-{canonicalKey[..16].ToLowerInvariant()}";
    private readonly record struct RegistrationIntent(string Email, string LegalName, NormalizedCuit Cuit);
    private sealed record ConfirmationEnvelope(Guid IdentityId, Guid TenantId, Guid MembershipId);
    private sealed class ExpectedIdentityValidationFailureException : Exception;
}

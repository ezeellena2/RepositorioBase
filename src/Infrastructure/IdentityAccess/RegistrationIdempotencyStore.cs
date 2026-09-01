using System.Buffers.Binary;
using System.Security.Cryptography;
using CleanArchitecture.Application.IdentityAccess.Organizations.RegisterOrganization;
using CleanArchitecture.Domain.IdentityAccess.Organizations;
using CleanArchitecture.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Infrastructure.IdentityAccess;

public sealed class RegistrationIdempotencyStore(ApplicationDbContext context, TimeProvider timeProvider) : IRegistrationIdempotencyStore
{
    public async Task<RegistrationSubmissionClaim> TryClaimAsync(string canonicalKey, CancellationToken cancellationToken)
    {
        var submission = RegistrationSubmission.Claim(canonicalKey, timeProvider.GetUtcNow());
        var affected = await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO registration_submissions ("Id", "CanonicalKey", "CreatedAt")
            VALUES ({submission.Id}, {submission.CanonicalKey}, {submission.CreatedAt})
            ON CONFLICT ("CanonicalKey") DO NOTHING;
            """, cancellationToken);
        if (affected == 1) return new RegistrationSubmissionClaim(submission, true);

        var existing = await context.RegistrationSubmissions
            .SingleAsync(candidate => candidate.CanonicalKey == canonicalKey, cancellationToken);
        return new RegistrationSubmissionClaim(existing, false);
    }

    public async Task CoordinateBusinessIntentAsync(string normalizedEmail, string normalizedCuit, CancellationToken cancellationToken)
    {
        var lockKeys = new[] { AdvisoryLockKey(normalizedEmail), AdvisoryLockKey(normalizedCuit) }
            .Distinct()
            .Order()
            .ToArray();

        foreach (var lockKey in lockKeys)
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({lockKey});", cancellationToken);
        }
    }

    private static long AdvisoryLockKey(string value) => BinaryPrimitives.ReadInt64BigEndian(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)).AsSpan(0, sizeof(long)));
}

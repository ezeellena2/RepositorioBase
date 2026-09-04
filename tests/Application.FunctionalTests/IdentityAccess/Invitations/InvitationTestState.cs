using CleanArchitecture.Application.FunctionalTests.Infrastructure;
using CleanArchitecture.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CleanArchitecture.Application.FunctionalTests.IdentityAccess.Invitations;

/// <summary>
/// Moves the single invitation of a test into a state its own use cases cannot reach quickly — a lapsed window, or
/// a withdrawal made outside the request under test. Written with SQL rather than the aggregate because the point
/// is the state the reader finds, not the transition that produced it.
/// </summary>
internal static class InvitationTestState
{
    /// <summary>Backdates the window so the invitation is lapsed without waiting for real time to pass.</summary>
    internal static Task ExpireAsync() =>
        ExecuteAsync("UPDATE \"Invitations\" SET \"CreatedAt\" = NOW() - INTERVAL '30 days', \"ExpiresAt\" = NOW() - INTERVAL '1 day';");

    /// <summary>Withdraws the offer the way <c>Cancel</c> does, leaving evidence the constraint accepts.</summary>
    internal static Task CancelAsync() =>
        ExecuteAsync("UPDATE \"Invitations\" SET \"Status\" = 'Cancelled', \"CancelledAt\" = GREATEST(\"CreatedAt\", NOW()), \"AcceptedAt\" = NULL, \"AcceptedByIdentityId\" = NULL;");

    private static async Task ExecuteAsync(string sql)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.Database.ExecuteSqlRawAsync(sql);
    }
}

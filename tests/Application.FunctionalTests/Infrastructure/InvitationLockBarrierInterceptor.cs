using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CleanArchitecture.Application.FunctionalTests.Infrastructure;

/// <summary>
/// Holds a read of the <c>Invitations</c> table until a second one arrives. Two requests started behind a barrier
/// still race only by luck — one can finish entirely before the other begins — so a concurrency test that does not
/// pin the overlap proves nothing. This makes both operations decide from the same committed state.
/// </summary>
public sealed class InvitationLockBarrierInterceptor : DbCommandInterceptor
{
    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        if (command.CommandText.Contains("\"Invitations\"", StringComparison.Ordinal))
        {
            await TestApp.WaitForInvitationLockBarrierAsync(cancellationToken);
        }

        return await base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }
}

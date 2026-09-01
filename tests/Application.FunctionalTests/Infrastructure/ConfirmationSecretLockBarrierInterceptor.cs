using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CleanArchitecture.Application.FunctionalTests.Infrastructure;

public sealed class ConfirmationSecretLockBarrierInterceptor : DbCommandInterceptor
{
    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        if (command.CommandText.Contains("outbox_secrets", StringComparison.OrdinalIgnoreCase) &&
            command.CommandText.Contains("FOR UPDATE", StringComparison.OrdinalIgnoreCase))
        {
            await TestApp.WaitForConfirmationSecretLockBarrierAsync(cancellationToken);
        }

        return await base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }
}

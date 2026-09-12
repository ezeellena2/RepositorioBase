using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CleanArchitecture.Application.FunctionalTests.Infrastructure;

/// <summary>
/// Rewrites an armed persistence command so PostgreSQL itself rejects it during execution. The failure therefore
/// crosses the same provider and EF diagnostics boundaries as a production database fault.
/// </summary>
public sealed class TestProviderFailureInterceptor : DbCommandInterceptor
{
    public const string SecretSentinel = "provider_secret_must_not_reach_logs";
    public const string ParameterName = "provider_password";
    public const string ParameterSentinel = "parameter_value_must_not_reach_logs";

    private const string InvalidCommand = $"SELECT * FROM \"{SecretSentinel}\" WHERE @{ParameterName} IS NULL";

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.CommandSource == CommandSource.SaveChanges && TestApp.ConsumeForcedUnexpectedFailure())
        {
            command.CommandText = InvalidCommand;
            command.Parameters.Clear();
            var parameter = command.CreateParameter();
            parameter.ParameterName = ParameterName;
            parameter.Value = ParameterSentinel;
            command.Parameters.Add(parameter);
        }

        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }
}

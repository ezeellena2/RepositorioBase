using Npgsql;
using Respawn;
using System.Data.Common;

namespace CleanArchitecture.Application.FunctionalTests.Infrastructure;

internal sealed class DatabaseResetter : IAsyncDisposable
{
    private readonly DbConnection _connection;
    private readonly Respawner _respawner;

    private DatabaseResetter(DbConnection connection, Respawner respawner)
    {
        _connection = connection;
        _respawner = respawner;
    }

    public static async Task<DatabaseResetter> CreateAsync(string connectionString)
    {
        var connection = new NpgsqlConnection(connectionString);

        await connection.OpenAsync();
        var respawner = await Respawner.CreateAsync(connection);
        await connection.CloseAsync();
        return new DatabaseResetter(connection, respawner);
    }

    public async Task ResetAsync()
    {
        await _connection.OpenAsync();
        try
        {
            await Execute("ALTER TABLE \"AuditEvents\" DISABLE TRIGGER USER");
            await _respawner.ResetAsync(_connection);
        }
        finally
        {
            await Execute("ALTER TABLE \"AuditEvents\" ENABLE TRIGGER USER");
            await _connection.CloseAsync();
        }
    }

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();

    private async Task Execute(string sql)
    {
        await using var command = _connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}

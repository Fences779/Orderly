using System.Data;
using Npgsql;
using Orderly.Server.Models;

namespace Orderly.Server.Data;

public sealed class PostgresConnectionFactory : IAsyncDisposable
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresConnectionFactory(ServerOptions options)
    {
        var builder = new NpgsqlConnectionStringBuilder(options.GetConnectionString());
        _dataSource = NpgsqlDataSource.Create(builder.ConnectionString);
    }

    public async Task<IDbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        return connection;
    }

    public ValueTask DisposeAsync() => _dataSource.DisposeAsync();
}

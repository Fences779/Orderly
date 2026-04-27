using Microsoft.Data.Sqlite;
using Orderly.Core.Models;
using Orderly.Core.Repositories;
using Orderly.Data.Sqlite;

namespace Orderly.Data.Repositories;

public sealed class ReplyTemplateRepository : IReplyTemplateRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public ReplyTemplateRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public Task<IReadOnlyList<ReplyTemplate>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return QueryAsync(false, cancellationToken);
    }

    public Task<IReadOnlyList<ReplyTemplate>> GetFavoritesAsync(CancellationToken cancellationToken = default)
    {
        return QueryAsync(true, cancellationToken);
    }

    private async Task<IReadOnlyList<ReplyTemplate>> QueryAsync(bool favoritesOnly, CancellationToken cancellationToken)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Title, Scene, Content, IsFavorite, SourcePlatform, CreatedAt, UpdatedAt
            FROM ReplyTemplates
            WHERE ($favoritesOnly = 0 OR IsFavorite = 1)
            ORDER BY IsFavorite DESC, UpdatedAt DESC;
            """;
        command.Parameters.AddWithValue("$favoritesOnly", favoritesOnly ? 1 : 0);

        var rows = new List<ReplyTemplate>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(Map(reader));
        }

        return rows;
    }

    private static ReplyTemplate Map(SqliteDataReader reader)
    {
        return new ReplyTemplate
        {
            Id = reader.GetInt32(0),
            Title = reader.GetString(1),
            Scene = reader.GetString(2),
            Content = reader.GetString(3),
            IsFavorite = reader.GetInt32(4) == 1,
            SourcePlatform = reader.GetString(5),
            CreatedAt = DateTime.Parse(reader.GetString(6)),
            UpdatedAt = DateTime.Parse(reader.GetString(7))
        };
    }
}

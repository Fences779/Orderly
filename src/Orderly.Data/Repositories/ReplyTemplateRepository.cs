using Microsoft.Data.Sqlite;
using Orderly.Core.Models;
using Orderly.Core.Repositories;
using Orderly.Core.Services;
using Orderly.Data.Services;
using Orderly.Data.Sqlite;

namespace Orderly.Data.Repositories;

public sealed class ReplyTemplateRepository : IReplyTemplateRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly IFieldEncryptionService _fieldEncryptionService;

    public ReplyTemplateRepository(SqliteConnectionFactory connectionFactory, IFieldEncryptionService fieldEncryptionService)
    {
        _connectionFactory = connectionFactory;
        _fieldEncryptionService = fieldEncryptionService ?? throw new ArgumentNullException(nameof(fieldEncryptionService));
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
            SELECT Id, Title, Scene, Content, ContentCiphertext, IsFavorite, SourcePlatform, CreatedAt, UpdatedAt
            FROM ReplyTemplates
            WHERE ($favoritesOnly = 0 OR IsFavorite = 1)
            ORDER BY IsFavorite DESC, UpdatedAt DESC;
            """;
        command.Parameters.AddWithValue("$favoritesOnly", favoritesOnly ? 1 : 0);

        var rows = new List<ReplyTemplate>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(Map(reader, _fieldEncryptionService));
        }

        return rows;
    }

    private static ReplyTemplate Map(SqliteDataReader reader, IFieldEncryptionService fieldEncryptionService)
    {
        var content = EncryptedColumnReader.ReadRequiredString(reader, 4, fieldEncryptionService, "ReplyTemplates.ContentCiphertext");

        return new ReplyTemplate
        {
            Id = reader.GetInt32(0),
            Title = reader.GetString(1),
            Scene = reader.GetString(2),
            Content = content,
            IsFavorite = reader.GetInt32(5) == 1,
            SourcePlatform = reader.GetString(6),
            CreatedAt = DateTime.Parse(reader.GetString(7)),
            UpdatedAt = DateTime.Parse(reader.GetString(8))
        };
    }
}

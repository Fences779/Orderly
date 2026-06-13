using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Repositories;
using Orderly.Data.Sqlite;

namespace Orderly.Data.Commerce.Repositories;

/// <summary>
/// Shared SQLCipher-backed implementation for the per-entity Commerce repositories
/// (Requirement 3.2). It owns the generic CRUD machinery — connection handling, parameterized SQL
/// generation, the shared audit/lifecycle columns, and the round-trip conversions — so each
/// concrete repository only declares its table name, its entity-specific columns, how to bind them,
/// and how to map a row back to an entity.
///
/// <para><b>Active queries exclude soft-deleted records (Requirement 2.9).</b> The default reads
/// (<see cref="GetByIdAsync"/>, <see cref="GetAllAsync"/>) append <c>WHERE DeletedAt IS NULL</c> so
/// archived/soft-deleted rows never appear. The <c>...IncludingDeleted</c> variants omit that filter
/// for recovery. <see cref="DeleteAsync"/> performs a recoverable soft-delete (sets <c>DeletedAt</c>
/// and the deleted lifecycle status) rather than physically removing the row.</para>
///
/// <para><b>Storage conventions.</b> Mirrors the Commerce schema: <c>Guid</c> ids and links as TEXT;
/// UTC timestamps as TEXT via the round-trip ("O") format; <c>EntityLifecycleStatus</c>, other enums,
/// and booleans as INTEGER; <see cref="CommerceMoney"/> and exact-decimal quantities as TEXT via an
/// invariant-culture round-trip; <c>CustomFieldsJson</c> as nullable TEXT persisted exactly as
/// provided once it passes the save-boundary well-formedness check below.</para>
///
/// <para><b>CustomFieldsJson save boundary (Requirement 3.11, 3.12).</b> Before any connection is
/// opened on <see cref="CreateAsync"/> or <see cref="UpdateAsync"/>, a non-null <c>CustomFieldsJson</c>
/// is parsed with <see cref="System.Text.Json.JsonDocument"/>; if it is not well-formed JSON the save
/// is rejected with <see cref="InvalidCustomFieldsException"/> before any write, leaving existing
/// persisted data unchanged. A null value is allowed and skips validation.</para>
///
/// <para>All SQL is parameterized; no value is ever interpolated into a command string.</para>
///
/// <para><b>Core_Write_Transaction enlistment (Req 18.1).</b> Every operation acquires its
/// connection through <see cref="AcquireConnectionAsync"/>. When a <see cref="CoreWriteTransaction"/>
/// is active on the current execution context, the operation runs on that transaction's single open
/// connection and attaches its command to the pending transaction, so a series of repository writes
/// commit or roll back atomically as one unit. With no ambient transaction the operation opens and
/// owns its own short-lived connection exactly as before. This keeps the repository contracts
/// unchanged while letting core writes such as order completion be all-or-nothing (Req 18.3).</para>
/// </summary>
/// <typeparam name="TEntity">The Universal_Domain_Model entity type this repository persists.</typeparam>
public abstract class CommerceRepositoryBase<TEntity> : ICommerceRepository<TEntity>
    where TEntity : CommerceEntity
{
    /// <summary>The shared audit/lifecycle/personalization columns present on every Commerce table.</summary>
    private static readonly string[] BaseColumnNames =
    {
        "Id", "CreatedAt", "UpdatedAt", "DeletedAt", "Lifecycle", "CustomFieldsJson",
    };

    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly string[] _allColumns;

    protected CommerceRepositoryBase(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _allColumns = BaseColumnNames.Concat(EntityColumns).ToArray();
    }

    /// <summary>
    /// Acquires the connection an operation should run on. When a <see cref="CoreWriteTransaction"/>
    /// is active on the current execution context, the operation borrows that transaction's single
    /// open connection (and its pending transaction) so the write enrolls in the atomic
    /// Core_Write_Transaction (Req 18.1); the borrowed connection is not owned and is left open on
    /// disposal. Otherwise a fresh connection is opened and owned, then disposed by the caller.
    /// </summary>
    private async Task<RepositoryConnection> AcquireConnectionAsync(CancellationToken cancellationToken)
    {
        CoreWriteTransaction? ambient = CoreWriteTransaction.Current;
        if (ambient is not null)
        {
            return new RepositoryConnection(ambient.Connection, ambient.Transaction, ownsConnection: false);
        }

        SqliteConnection connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        return new RepositoryConnection(connection, transaction: null, ownsConnection: true);
    }

    /// <summary>
    /// A leased connection plus the transaction (if any) commands must be attached to. Owns and
    /// disposes the connection only when it was opened for a single operation; a connection borrowed
    /// from an ambient <see cref="CoreWriteTransaction"/> is left open for the rest of that transaction.
    /// </summary>
    private readonly struct RepositoryConnection : IAsyncDisposable
    {
        private readonly bool _ownsConnection;

        public RepositoryConnection(SqliteConnection connection, SqliteTransaction? transaction, bool ownsConnection)
        {
            Connection = connection;
            Transaction = transaction;
            _ownsConnection = ownsConnection;
        }

        public SqliteConnection Connection { get; }

        public SqliteTransaction? Transaction { get; }

        public ValueTask DisposeAsync()
            => _ownsConnection ? Connection.DisposeAsync() : ValueTask.CompletedTask;
    }

    /// <summary>The single source of truth for this repository's table name (matches the Commerce schema).</summary>
    protected abstract string TableName { get; }

    /// <summary>
    /// The entity-specific column names in schema order, following the six shared base columns. For
    /// workspace-scoped entities this list begins with <c>WorkspaceId</c>.
    /// </summary>
    protected abstract IReadOnlyList<string> EntityColumns { get; }

    /// <summary>Binds one parameter named <c>$&lt;column&gt;</c> for each entity-specific column.</summary>
    protected abstract void BindEntity(SqliteCommand command, TEntity entity);

    /// <summary>
    /// Reconstructs an entity from a fully-selected row. Implementations must set every init-only
    /// field (including <c>Id</c>, <c>CreatedAt</c>, and any <c>WorkspaceId</c>) and the
    /// <see cref="CommerceEntity.CustomFieldsJson"/> value; the base then restores the audit fields.
    /// </summary>
    protected abstract TEntity MapEntity(SqliteDataReader reader);

    public async Task<TEntity> CreateAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ValidateCustomFields(entity);

        string columnList = string.Join(", ", _allColumns.Select(Quote));
        string valueList = string.Join(", ", _allColumns.Select(column => "$" + column));

        await using RepositoryConnection lease = await AcquireConnectionAsync(cancellationToken);
        await using SqliteCommand command = lease.Connection.CreateCommand();
        command.Transaction = lease.Transaction;
        command.CommandText = $"INSERT INTO {Quote(TableName)} ({columnList}) VALUES ({valueList});";
        BindBase(command, entity);
        BindEntity(command, entity);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return entity;
    }

    public Task<TEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => GetByIdCoreAsync(id, includeDeleted: false, cancellationToken);

    public Task<TEntity?> GetByIdIncludingDeletedAsync(Guid id, CancellationToken cancellationToken = default)
        => GetByIdCoreAsync(id, includeDeleted: true, cancellationToken);

    public Task<IReadOnlyList<TEntity>> GetAllAsync(CancellationToken cancellationToken = default)
        => GetAllCoreAsync(includeDeleted: false, cancellationToken);

    public Task<IReadOnlyList<TEntity>> GetAllIncludingDeletedAsync(CancellationToken cancellationToken = default)
        => GetAllCoreAsync(includeDeleted: true, cancellationToken);

    public async Task UpdateAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ValidateCustomFields(entity);

        // Persist every column except the immutable Id and CreatedAt.
        IEnumerable<string> updatable = _allColumns.Where(c => c is not ("Id" or "CreatedAt"));
        string setClause = string.Join(", ", updatable.Select(c => $"{Quote(c)} = ${c}"));

        await using RepositoryConnection lease = await AcquireConnectionAsync(cancellationToken);
        await using SqliteCommand command = lease.Connection.CreateCommand();
        command.Transaction = lease.Transaction;
        command.CommandText = $"UPDATE {Quote(TableName)} SET {setClause} WHERE {Quote("Id")} = $Id AND {Quote("DeletedAt")} IS NULL;";
        BindBase(command, entity);
        BindEntity(command, entity);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        string now = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);

        await using RepositoryConnection lease = await AcquireConnectionAsync(cancellationToken);
        await using SqliteCommand command = lease.Connection.CreateCommand();
        command.Transaction = lease.Transaction;
        command.CommandText =
            $"UPDATE {Quote(TableName)} SET {Quote("DeletedAt")} = $deletedAt, {Quote("Lifecycle")} = $lifecycle, {Quote("UpdatedAt")} = $updatedAt " +
            $"WHERE {Quote("Id")} = $id AND {Quote("DeletedAt")} IS NULL;";
        command.Parameters.AddWithValue("$deletedAt", now);
        command.Parameters.AddWithValue("$lifecycle", (int)EntityLifecycleStatus.Deleted);
        command.Parameters.AddWithValue("$updatedAt", now);
        command.Parameters.AddWithValue("$id", id.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<TEntity?> GetByIdCoreAsync(Guid id, bool includeDeleted, CancellationToken cancellationToken)
    {
        string columnList = string.Join(", ", _allColumns.Select(Quote));
        string activeFilter = includeDeleted ? string.Empty : $" AND {Quote("DeletedAt")} IS NULL";

        await using RepositoryConnection lease = await AcquireConnectionAsync(cancellationToken);
        await using SqliteCommand command = lease.Connection.CreateCommand();
        command.Transaction = lease.Transaction;
        command.CommandText = $"SELECT {columnList} FROM {Quote(TableName)} WHERE {Quote("Id")} = $id{activeFilter};";
        command.Parameters.AddWithValue("$id", id.ToString());

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Map(reader) : null;
    }

    private async Task<IReadOnlyList<TEntity>> GetAllCoreAsync(bool includeDeleted, CancellationToken cancellationToken)
    {
        string columnList = string.Join(", ", _allColumns.Select(Quote));
        string activeFilter = includeDeleted ? string.Empty : $" WHERE {Quote("DeletedAt")} IS NULL";

        await using RepositoryConnection lease = await AcquireConnectionAsync(cancellationToken);
        await using SqliteCommand command = lease.Connection.CreateCommand();
        command.Transaction = lease.Transaction;
        command.CommandText = $"SELECT {columnList} FROM {Quote(TableName)}{activeFilter} ORDER BY {Quote("CreatedAt")} ASC;";

        var rows = new List<TEntity>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(Map(reader));
        }

        return rows;
    }

    private TEntity Map(SqliteDataReader reader)
    {
        // The concrete repository sets all init-only fields and CustomFieldsJson; restoring the audit
        // state last fixes UpdatedAt/DeletedAt/Lifecycle exactly as stored (the CustomFieldsJson setter
        // would otherwise advance UpdatedAt during rehydration).
        TEntity entity = MapEntity(reader);
        entity.RestoreAuditState(
            GetDateTime(reader, "UpdatedAt"),
            GetDateTimeNullable(reader, "DeletedAt"),
            (EntityLifecycleStatus)reader.GetInt32(reader.GetOrdinal("Lifecycle")));
        return entity;
    }

    /// <summary>
    /// Enforces the <c>CustomFieldsJson</c> save-boundary rule (Requirement 3.11, 3.12). A null value
    /// is allowed and skips validation; a non-null value must be well-formed JSON. The check runs
    /// before any connection is opened or row is written, so a rejected save leaves existing persisted
    /// data unchanged (no partial write).
    /// </summary>
    /// <exception cref="InvalidCustomFieldsException">
    /// Thrown when <c>CustomFieldsJson</c> is non-null and is not well-formed JSON.
    /// </exception>
    private static void ValidateCustomFields(TEntity entity)
    {
        string? customFieldsJson = entity.CustomFieldsJson;
        if (customFieldsJson is null)
        {
            return;
        }

        try
        {
            using JsonDocument _ = JsonDocument.Parse(customFieldsJson);
        }
        catch (JsonException ex)
        {
            throw new InvalidCustomFieldsException(
                "无法保存：自定义字段内容不是有效的 JSON。", // "Cannot save: custom-field content is not valid JSON."
                ex);
        }
    }

    /// <summary>Binds the six shared base-column parameters from the entity.</summary>
    private static void BindBase(SqliteCommand command, TEntity entity)
    {
        command.Parameters.AddWithValue("$Id", entity.Id.ToString());
        command.Parameters.AddWithValue("$CreatedAt", entity.CreatedAt.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$UpdatedAt", entity.UpdatedAt.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$DeletedAt", DateTimeToDb(entity.DeletedAt));
        command.Parameters.AddWithValue("$Lifecycle", (int)entity.Lifecycle);
        command.Parameters.AddWithValue("$CustomFieldsJson", (object?)entity.CustomFieldsJson ?? DBNull.Value);
    }

    // ----- Shared write conversions (entity -> DB value) -----

    protected static string MoneyToDb(CommerceMoney money)
        => money.Amount.ToString("0.00", CultureInfo.InvariantCulture);

    protected static object MoneyToDb(CommerceMoney? money)
        => money is null ? DBNull.Value : money.Value.Amount.ToString("0.00", CultureInfo.InvariantCulture);

    protected static string DecimalToDb(decimal value)
        => value.ToString(CultureInfo.InvariantCulture);

    protected static string DateTimeToDb(DateTime value)
        => value.ToString("O", CultureInfo.InvariantCulture);

    protected static object DateTimeToDb(DateTime? value)
        => value is null ? DBNull.Value : value.Value.ToString("O", CultureInfo.InvariantCulture);

    protected static string GuidToDb(Guid value)
        => value.ToString();

    protected static object GuidToDb(Guid? value)
        => value is null ? DBNull.Value : value.Value.ToString();

    protected static object TextToDb(string? value)
        => (object?)value ?? DBNull.Value;

    // ----- Shared read conversions (DB value -> entity) -----

    protected static string GetString(SqliteDataReader reader, string column)
        => reader.GetString(reader.GetOrdinal(column));

    protected static string? GetStringNullable(SqliteDataReader reader, string column)
    {
        int ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    protected static Guid GetGuid(SqliteDataReader reader, string column)
        => Guid.Parse(reader.GetString(reader.GetOrdinal(column)));

    protected static Guid? GetGuidNullable(SqliteDataReader reader, string column)
    {
        int ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : Guid.Parse(reader.GetString(ordinal));
    }

    protected static DateTime GetDateTime(SqliteDataReader reader, string column)
        => DateTime.Parse(reader.GetString(reader.GetOrdinal(column)), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    protected static DateTime? GetDateTimeNullable(SqliteDataReader reader, string column)
    {
        int ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal)
            ? null
            : DateTime.Parse(reader.GetString(ordinal), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }

    protected static CommerceMoney GetMoney(SqliteDataReader reader, string column)
        => CommerceMoney.From(decimal.Parse(reader.GetString(reader.GetOrdinal(column)), NumberStyles.Number, CultureInfo.InvariantCulture));

    protected static CommerceMoney? GetMoneyNullable(SqliteDataReader reader, string column)
    {
        int ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal)
            ? null
            : CommerceMoney.From(decimal.Parse(reader.GetString(ordinal), NumberStyles.Number, CultureInfo.InvariantCulture));
    }

    protected static decimal GetDecimal(SqliteDataReader reader, string column)
        => decimal.Parse(reader.GetString(reader.GetOrdinal(column)), NumberStyles.Number, CultureInfo.InvariantCulture);

    protected static int GetInt(SqliteDataReader reader, string column)
        => reader.GetInt32(reader.GetOrdinal(column));

    protected static bool GetBool(SqliteDataReader reader, string column)
        => reader.GetInt32(reader.GetOrdinal(column)) != 0;

    protected static TEnum GetEnum<TEnum>(SqliteDataReader reader, string column)
        where TEnum : struct, Enum
        => (TEnum)Enum.ToObject(typeof(TEnum), reader.GetInt32(reader.GetOrdinal(column)));

    /// <summary>Quotes an identifier that originates only from our own column/table constants.</summary>
    private static string Quote(string identifier)
    {
        foreach (char character in identifier)
        {
            bool isLetter = character is >= 'A' and <= 'Z' or >= 'a' and <= 'z';
            bool isDigit = character is >= '0' and <= '9';
            if (!isLetter && !isDigit && character != '_')
            {
                throw new InvalidOperationException("SQLite identifier is invalid.");
            }
        }

        return "\"" + identifier + "\"";
    }
}

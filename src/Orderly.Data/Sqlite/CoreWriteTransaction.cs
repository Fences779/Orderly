using Microsoft.Data.Sqlite;

namespace Orderly.Data.Sqlite;

/// <summary>
/// The project's single-transaction abstraction — the <c>Core_Write_Transaction</c> from the
/// design (Req 18.1). A core business write (such as order completion) opens one of these, performs
/// all of its reads and writes through the ordinary Commerce repositories, and commits only when
/// every step succeeds. If the operation is abandoned (an exception, an early return, or an explicit
/// failure) without a commit, disposal rolls the whole transaction back so the data is left exactly
/// as it was before the operation began, with no partial update (Req 18.3).
///
/// <para><b>How repositories enlist.</b> A begun transaction is published as an <i>ambient</i>
/// transaction on the current asynchronous execution context (<see cref="AsyncLocal{T}"/>). While it
/// is active, every <see cref="Orderly.Data.Commerce.Repositories.CommerceRepositoryBase{TEntity}"/>
/// operation runs on this transaction's single open connection and enrolls its command in this
/// transaction instead of opening its own connection. This lets the existing repositories take part
/// in the atomic write without any change to their public contracts. Reads performed before the
/// transaction is begun continue to use their own short-lived connections.</para>
///
/// <para><b>Why <see cref="Begin"/> is synchronous.</b> An <see cref="AsyncLocal{T}"/> value set
/// inside an <c>async</c> method is not observed by that method's caller, because the caller resumes
/// on the execution context captured before the call. The ambient transaction must therefore be
/// published synchronously, in the caller's own context, so the repository operations that follow can
/// see it. Opening a local SQLite connection and beginning a transaction are fast in-process
/// operations, so doing them synchronously is appropriate. The transaction is ended with a
/// synchronous <see cref="Dispose"/> (via a plain <c>using</c>) for the same reason: clearing the
/// ambient must happen in the caller's context.</para>
///
/// <para><b>Encryption is preserved (C-2).</b> The connection is obtained from the same
/// <see cref="SqliteConnectionFactory"/> the repositories use, so the SQLCipher key is applied on
/// open exactly as for every other connection; this abstraction adds only the surrounding
/// transaction and never bypasses the encrypted connection path.</para>
///
/// <para>Nested transactions are not supported: beginning a second transaction while one is already
/// active on the current context throws. The type is single-use and is not safe for concurrent use
/// across threads; each core write begins, uses, and disposes its own instance.</para>
/// </summary>
public sealed class CoreWriteTransaction : IDisposable
{
    private static readonly AsyncLocal<CoreWriteTransaction?> AmbientHolder = new();

    private readonly SqliteConnection _connection;
    private readonly SqliteTransaction _transaction;
    private bool _committed;
    private bool _disposed;

    private CoreWriteTransaction(SqliteConnection connection, SqliteTransaction transaction)
    {
        _connection = connection;
        _transaction = transaction;
    }

    /// <summary>
    /// The transaction currently active on this asynchronous execution context, or <c>null</c> when
    /// no core write is in progress. The repository base consults this to decide whether to enlist in
    /// an ambient transaction or open its own connection.
    /// </summary>
    internal static CoreWriteTransaction? Current => AmbientHolder.Value;

    /// <summary>The single open connection shared by every repository operation inside this transaction.</summary>
    internal SqliteConnection Connection => _connection;

    /// <summary>The pending transaction every enlisted command must be attached to.</summary>
    internal SqliteTransaction Transaction => _transaction;

    /// <summary>
    /// Opens a connection from <paramref name="connectionFactory"/> (applying the SQLCipher key on
    /// open), begins a database transaction, and publishes it as the ambient transaction for the
    /// current execution context. Dispose the returned instance — with a plain <c>using</c> — to end
    /// the transaction; without an intervening <see cref="CommitAsync"/> the disposal rolls
    /// everything back.
    /// </summary>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="connectionFactory"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when a transaction is already active on the current context.</exception>
    public static CoreWriteTransaction Begin(SqliteConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);

        if (AmbientHolder.Value is not null)
        {
            throw new InvalidOperationException(
                "A Core_Write_Transaction is already active on the current execution context; nested transactions are not supported.");
        }

        SqliteConnection connection = connectionFactory.CreateConnection();
        try
        {
            connection.Open();

            // SQLite serializes writers. While this transaction holds the database for the duration of
            // a core write, other short-lived repository connections may momentarily hold a shared
            // lock; a busy timeout makes the writer wait for those to clear instead of failing
            // immediately with "database is locked".
            using (SqliteCommand busyTimeout = connection.CreateCommand())
            {
                busyTimeout.CommandText = "PRAGMA busy_timeout = 5000;";
                busyTimeout.ExecuteNonQuery();
            }

            var transaction = (SqliteTransaction)connection.BeginTransaction();
            var scope = new CoreWriteTransaction(connection, transaction);
            AmbientHolder.Value = scope;
            return scope;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Commits every write performed inside the transaction. After a successful commit, disposal no
    /// longer rolls anything back.
    /// </summary>
    /// <exception cref="ObjectDisposedException">Thrown when the transaction has already been disposed.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the transaction has already been committed.</exception>
    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_committed)
        {
            throw new InvalidOperationException("This Core_Write_Transaction has already been committed.");
        }

        await _transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        _committed = true;
    }

    /// <summary>
    /// Ends the transaction. If it was not committed, the pending transaction is rolled back so all
    /// data is left in its pre-operation state (Req 18.3). The ambient slot is always cleared and the
    /// connection released. Runs synchronously so the ambient clear takes effect in the caller's
    /// execution context.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (ReferenceEquals(AmbientHolder.Value, this))
        {
            AmbientHolder.Value = null;
        }

        try
        {
            if (!_committed)
            {
                _transaction.Rollback();
            }
        }
        catch (SqliteException)
        {
            // The transaction may already be unusable (for example, the connection faulted mid-write).
            // Disposal must never mask the original failure, so a rollback failure here is swallowed.
        }
        finally
        {
            _transaction.Dispose();
            _connection.Dispose();
        }
    }
}

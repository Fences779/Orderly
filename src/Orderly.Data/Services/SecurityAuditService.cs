using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Orderly.Core.Models;
using Orderly.Core.Services;
using Orderly.Data.Sqlite;

namespace Orderly.Data.Services;

/// <summary>
/// <see cref="ISecurityAuditService"/> 的后端实现（需求 2.4 / design Property 4、BC-6）。
///
/// 防篡改思路：追加写 + 链式哈希。
/// - 每条记录的 <c>PreviousHash</c> 指向上一条记录的 <c>RecordHash</c>（首条指向创世哈希）。
/// - <c>RecordHash</c> = SHA-256(规范化字段串 ‖ PreviousHash)。
/// - 任意历史记录被篡改/删除/重排都会破坏其后所有记录的哈希链，可通过校验检出。
///
/// 同步内存链（<see cref="Record"/> / <see cref="GetRecords"/> / <see cref="VerifyChainIntegrity"/>）保留既有
/// Property 4 行为；异步接缝（<see cref="RecordAsync"/> / <see cref="QueryAsync"/>，BC-6 / 任务 11.3）则将审计
/// 记录持久化到会话数据密钥加密的本账号库（全库 SQLCipher 加密），同样以追加式 + 链式完整性哈希保持防篡改，
/// 仅存事件类型 / 时间 / 账号标签 / 脱敏 detail，绝不落明文凭证，且全量保留不截断、不自动清除。
///
/// 线程安全：同步内存链在 <see cref="_syncRoot"/> 下进行；异步持久化写入在 <see cref="_writeGate"/> 下串行，
/// 保证序号与哈希链的原子追加。
/// </summary>
public sealed class SecurityAuditService : ISecurityAuditService
{
    // 创世哈希（64 个 '0'），作为首条记录的 PreviousHash。
    private const string GenesisHash = "0000000000000000000000000000000000000000000000000000000000000000";

    // 主体哈希的域分隔前缀，避免与其他哈希用途产生交叉。
    private const string SubjectHashDomain = "orderly.security-audit.subject.v1:";

    // 空主体的中性占位（仍以哈希形式存储，不暴露原因）。
    private const string AnonymousSubject = "<anonymous>";

    // 持久化审计表与字段间分隔符（不可出现在各字段内容中，规范化时已剥离控制字符）。
    private const string PersistentHashDomain = "orderly.security-audit.entry.v1:";
    private const char FieldSeparator = '\u001f';
    private const int MaxAccountLabelCharacters = 160;
    private const int MaxDetailCharacters = 2000;

    private readonly object _syncRoot = new();
    private readonly List<SecurityAuditRecord> _records = new();
    private string _lastHash = GenesisHash;
    private long _nextSequence;

    // 加密本地存储（SQLCipher）连接工厂提供者。返回 null 表示当前无可用加密库（如未登录 / 数据密钥不可用），
    // 此时异步写入「尽力而为」地降级为无操作，读取返回空列表，绝不抛出异常影响调用方控制流。
    private readonly Func<SqliteConnectionFactory?>? _encryptedStoreProvider;
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    /// <summary>
    /// 无持久化后端的构造（向后兼容既有 fallback 调用点）。异步写入接缝降级为无操作，读取返回空列表。
    /// 持久化由接受加密库连接来源的重载启用（认证 / 账户服务层接线见任务 11.5）。
    /// </summary>
    public SecurityAuditService()
        : this(encryptedStoreProvider: null)
    {
    }

    /// <summary>
    /// 经会话上下文解析加密本账号库的构造。审计记录写入 <see cref="ISessionContextService.Current"/> 指向的
    /// 数据库（以会话数据密钥进行全库 SQLCipher 加密）。
    /// </summary>
    public SecurityAuditService(ISessionContextService sessionContextService)
        : this(BuildSessionStoreProvider(sessionContextService))
    {
    }

    /// <summary>
    /// 经显式加密库连接工厂提供者的构造，便于测试与解耦接线。<paramref name="encryptedStoreProvider"/>
    /// 每次调用应返回指向加密本地库的连接工厂；返回 null 表示当前无可用加密库。
    /// </summary>
    public SecurityAuditService(Func<SqliteConnectionFactory?>? encryptedStoreProvider)
    {
        _encryptedStoreProvider = encryptedStoreProvider;
    }

    public SecurityAuditRecord Record(SecurityEventType eventType, string? subject, SecurityEventOutcome outcome)
    {
        lock (_syncRoot)
        {
            var sequence = _nextSequence;
            var occurredAt = DateTimeOffset.UtcNow;
            var subjectIdentifier = HashSubject(subject);
            var previousHash = _lastHash;
            var recordHash = ComputeRecordHash(sequence, eventType, occurredAt, subjectIdentifier, outcome, previousHash);

            var record = new SecurityAuditRecord
            {
                Sequence = sequence,
                EventType = eventType,
                OccurredAt = occurredAt,
                SubjectIdentifier = subjectIdentifier,
                Outcome = outcome,
                PreviousHash = previousHash,
                RecordHash = recordHash,
            };

            _records.Add(record);
            _lastHash = recordHash;
            _nextSequence++;
            return record;
        }
    }

    public IReadOnlyList<SecurityAuditRecord> GetRecords()
    {
        lock (_syncRoot)
        {
            return _records.ToArray();
        }
    }

    public bool VerifyChainIntegrity()
    {
        lock (_syncRoot)
        {
            var expectedPrevious = GenesisHash;
            var expectedSequence = 0L;

            foreach (var record in _records)
            {
                if (record.Sequence != expectedSequence)
                {
                    return false;
                }

                if (!string.Equals(record.PreviousHash, expectedPrevious, StringComparison.Ordinal))
                {
                    return false;
                }

                var recomputed = ComputeRecordHash(
                    record.Sequence,
                    record.EventType,
                    record.OccurredAt,
                    record.SubjectIdentifier,
                    record.Outcome,
                    record.PreviousHash);

                if (!string.Equals(record.RecordHash, recomputed, StringComparison.Ordinal))
                {
                    return false;
                }

                expectedPrevious = record.RecordHash;
                expectedSequence++;
            }

            return true;
        }
    }

    /// <summary>
    /// BC-6 / 任务 11.3：将一条安全审计记录追加写入加密本地存储（SQLCipher）。
    ///
    /// 防篡改：以单调递增 <c>Sequence</c> 固定顺序，<c>PreviousHash</c> 链接上一条记录的 <c>RecordHash</c>，
    /// <c>RecordHash</c> 覆盖本条全部字段与 <c>PreviousHash</c>；仅追加、绝不更新或删除历史，全量保留不截断。
    /// 隐私：仅记录事件类型、时间（UTC）、账号标签与脱敏 detail，绝不接收 / 记录明文凭证（密码 / PIN 原文）。
    /// 语义：「尽力而为」，审计写入失败不抛出、不改变调用方原有控制流。
    /// </summary>
    public async Task RecordAsync(SecurityAuditEventKind kind, string accountLabel, string detail, CancellationToken ct = default)
    {
        if (_encryptedStoreProvider is null)
        {
            return;
        }

        SqliteConnectionFactory? factory;
        try
        {
            factory = _encryptedStoreProvider();
        }
        catch
        {
            return;
        }

        if (factory is null)
        {
            return;
        }

        var label = NormalizeField(accountLabel, MaxAccountLabelCharacters);
        var sanitizedDetail = NormalizeField(detail, MaxDetailCharacters);

        try
        {
            await _writeGate.WaitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            await using var connection = factory.CreateConnection();
            await connection.OpenAsync(ct).ConfigureAwait(false);
            await EnsureTableAsync(connection, ct).ConfigureAwait(false);

            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

            var (nextSequence, previousHash) = await ReadChainHeadAsync(connection, transaction, ct).ConfigureAwait(false);
            var occurredAt = DateTimeOffset.UtcNow;
            var recordHash = ComputePersistentHash(nextSequence, kind, occurredAt, label, sanitizedDetail, previousHash);

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO SecurityAuditEntries (Sequence, Kind, OccurredAt, AccountLabel, Detail, PreviousHash, RecordHash)
                VALUES ($sequence, $kind, $occurredAt, $accountLabel, $detail, $previousHash, $recordHash);
                """;
            command.Parameters.AddWithValue("$sequence", nextSequence);
            command.Parameters.AddWithValue("$kind", (int)kind);
            command.Parameters.AddWithValue("$occurredAt", occurredAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$accountLabel", label);
            command.Parameters.AddWithValue("$detail", sanitizedDetail);
            command.Parameters.AddWithValue("$previousHash", previousHash);
            command.Parameters.AddWithValue("$recordHash", recordHash);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            // 「尽力而为」：审计写入失败不得改变调用方原有控制流。
        }
        finally
        {
            _writeGate.Release();
        }
    }

    /// <summary>
    /// BC-6 / BC-14（任务 11.4）异步读取 API。从加密本地存储按 <c>Sequence</c> 升序读取，按账号标签与时间范围
    /// （<paramref name="from"/> / <paramref name="to"/>）筛选，返回顺序稳定的子集；空结果返回空列表而非抛异常。
    /// 全量保留：底层不截断或自动清除历史，查询仅作只读筛选、绝不改变底层记录。
    ///
    /// 边界语义（含边界）：仅返回满足 <c>OccurredAt &gt;= from</c> 且 <c>OccurredAt &lt;= to</c> 的记录；
    /// <paramref name="from"/> / <paramref name="to"/> 为 null 表示该侧不限。
    ///
    /// 时区口径（与存储一致，统一按 UTC 比较）：审计时间以 UTC（O 格式）存储，读取后为 <see cref="DateTimeKind.Utc"/>。
    /// 比较前将 <paramref name="from"/> / <paramref name="to"/> 统一归一到 UTC：<see cref="DateTimeKind.Local"/> 按本地时区
    /// 转换为 UTC（<see cref="DateTime.ToUniversalTime"/>）；<see cref="DateTimeKind.Unspecified"/> 视为 UTC 边界
    /// （调用方约定以 UTC 口径传入）。这样可避免 <see cref="DateTime"/> 运算符仅比较 Ticks、忽略 <see cref="DateTime.Kind"/>
    /// 而在有时差时区下产生的边界误判。
    /// </summary>
    public async Task<IReadOnlyList<SecurityAuditEntry>> QueryAsync(
        string? accountLabel = null,
        DateTime? from = null,
        DateTime? to = null,
        CancellationToken ct = default)
    {
        if (_encryptedStoreProvider is null)
        {
            return Array.Empty<SecurityAuditEntry>();
        }

        SqliteConnectionFactory? factory;
        try
        {
            factory = _encryptedStoreProvider();
        }
        catch
        {
            return Array.Empty<SecurityAuditEntry>();
        }

        if (factory is null)
        {
            return Array.Empty<SecurityAuditEntry>();
        }

        try
        {
            await using var connection = factory.CreateConnection();
            await connection.OpenAsync(ct).ConfigureAwait(false);
            await EnsureTableAsync(connection, ct).ConfigureAwait(false);

            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT Kind, OccurredAt, AccountLabel, Detail
                FROM SecurityAuditEntries
                ORDER BY Sequence ASC;
                """;

            var labelFilter = string.IsNullOrWhiteSpace(accountLabel)
                ? null
                : NormalizeField(accountLabel, MaxAccountLabelCharacters);

            // 统一归一到 UTC 口径再比较，避免 DateTime 运算符忽略 Kind 导致的跨时区边界误判。
            var fromUtc = from is { } rawFrom ? NormalizeBoundToUtc(rawFrom) : (DateTime?)null;
            var toUtc = to is { } rawTo ? NormalizeBoundToUtc(rawTo) : (DateTime?)null;

            var results = new List<SecurityAuditEntry>();
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var kindValue = reader.GetInt32(0);
                var occurredAt = DateTime.Parse(reader.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
                var label = reader.GetString(2);
                var detail = reader.GetString(3);

                if (labelFilter is not null && !string.Equals(label, labelFilter, StringComparison.Ordinal))
                {
                    continue;
                }

                // 含下界：仅保留 OccurredAt >= from。
                if (fromUtc is { } lower && occurredAt < lower)
                {
                    continue;
                }

                // 含上界：仅保留 OccurredAt <= to。
                if (toUtc is { } upper && occurredAt > upper)
                {
                    continue;
                }

                results.Add(new SecurityAuditEntry(occurredAt, KindToLabel(kindValue), label, detail));
            }

            return results;
        }
        catch
        {
            return Array.Empty<SecurityAuditEntry>();
        }
    }

    /// <summary>
    /// 校验持久化审计链的完整性（防篡改自检）。从加密本地存储按 <c>Sequence</c> 升序重算哈希链，
    /// 若序号连续、<c>PreviousHash</c> 正确链接且 <c>RecordHash</c> 与重算一致则返回 true；
    /// 任意历史记录被篡改 / 删除 / 重排都会使其返回 false。无记录视为完整（返回 true）。
    /// </summary>
    public async Task<bool> VerifyPersistedChainIntegrityAsync(CancellationToken ct = default)
    {
        if (_encryptedStoreProvider is null)
        {
            return true;
        }

        var factory = _encryptedStoreProvider();
        if (factory is null)
        {
            return true;
        }

        await using var connection = factory.CreateConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await EnsureTableAsync(connection, ct).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Sequence, Kind, OccurredAt, AccountLabel, Detail, PreviousHash, RecordHash
            FROM SecurityAuditEntries
            ORDER BY Sequence ASC;
            """;

        var expectedPrevious = GenesisHash;
        var expectedSequence = 0L;

        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var sequence = reader.GetInt64(0);
            var kind = (SecurityAuditEventKind)reader.GetInt32(1);
            var occurredAt = DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            var label = reader.GetString(3);
            var detail = reader.GetString(4);
            var previousHash = reader.GetString(5);
            var recordHash = reader.GetString(6);

            if (sequence != expectedSequence)
            {
                return false;
            }

            if (!string.Equals(previousHash, expectedPrevious, StringComparison.Ordinal))
            {
                return false;
            }

            var recomputed = ComputePersistentHash(sequence, kind, occurredAt, label, detail, previousHash);
            if (!string.Equals(recordHash, recomputed, StringComparison.Ordinal))
            {
                return false;
            }

            expectedPrevious = recordHash;
            expectedSequence++;
        }

        return true;
    }

    private static Func<SqliteConnectionFactory?> BuildSessionStoreProvider(ISessionContextService sessionContextService)
    {
        ArgumentNullException.ThrowIfNull(sessionContextService);
        return () =>
        {
            var session = sessionContextService.Current;
            if (session is null || string.IsNullOrWhiteSpace(session.DatabasePath))
            {
                return null;
            }

            if (!sessionContextService.IsDataKeyAvailable)
            {
                return null;
            }

            return new SqliteConnectionFactory(session.DatabasePath, () =>
            {
                var key = sessionContextService.Current?.DataKey;
                return key is { Length: SqliteConnectionKeying.RawKeyByteLength } ? key.ToArray() : null;
            });
        };
    }

    private static async Task EnsureTableAsync(SqliteConnection connection, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS SecurityAuditEntries (
                Sequence INTEGER PRIMARY KEY,
                Kind INTEGER NOT NULL,
                OccurredAt TEXT NOT NULL,
                AccountLabel TEXT NOT NULL DEFAULT '',
                Detail TEXT NOT NULL DEFAULT '',
                PreviousHash TEXT NOT NULL,
                RecordHash TEXT NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task<(long NextSequence, string PreviousHash)> ReadChainHeadAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT Sequence, RecordHash
            FROM SecurityAuditEntries
            ORDER BY Sequence DESC
            LIMIT 1;
            """;

        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var lastSequence = reader.GetInt64(0);
            var lastHash = reader.GetString(1);
            return (lastSequence + 1, lastHash);
        }

        return (0L, GenesisHash);
    }

    private static string NormalizeField(string? value, int maxCharacters)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        // 剥离所有控制字符（含字段分隔符 \u001f），既避免泄露异常排版，也保证规范化哈希串无歧义。
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (!char.IsControl(ch))
            {
                builder.Append(ch);
            }
        }

        var normalized = builder.ToString().Trim();
        if (normalized.Length > maxCharacters)
        {
            normalized = normalized[..maxCharacters];
        }

        return normalized;
    }

    private static string KindToLabel(int kindValue)
    {
        // 数据层返回稳定的不变量名（如 "LoginSucceeded"）；面向用户的中文展示映射由 ViewModel 层负责。
        return Enum.IsDefined(typeof(SecurityAuditEventKind), kindValue)
            ? ((SecurityAuditEventKind)kindValue).ToString()
            : kindValue.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// 将日期范围边界统一归一到 UTC，使其与以 UTC 存储的 <c>OccurredAt</c> 比较口径一致：
    /// <see cref="DateTimeKind.Local"/> 按本地时区转换为 UTC；<see cref="DateTimeKind.Utc"/> 原样返回；
    /// <see cref="DateTimeKind.Unspecified"/> 视为 UTC（调用方约定以 UTC 口径传入边界）。
    /// </summary>
    private static DateTime NormalizeBoundToUtc(DateTime bound)
    {
        return bound.Kind switch
        {
            DateTimeKind.Local => bound.ToUniversalTime(),
            DateTimeKind.Unspecified => DateTime.SpecifyKind(bound, DateTimeKind.Utc),
            _ => bound,
        };
    }

    private static string HashSubject(string? subject)
    {
        var normalized = string.IsNullOrWhiteSpace(subject)
            ? AnonymousSubject
            : subject.Trim().ToLowerInvariant();

        var bytes = Encoding.UTF8.GetBytes(SubjectHashDomain + normalized);
        try
        {
            return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private static string ComputeRecordHash(
        long sequence,
        SecurityEventType eventType,
        DateTimeOffset occurredAt,
        string subjectIdentifier,
        SecurityEventOutcome outcome,
        string previousHash)
    {
        // 规范化为确定性字符串，字段间使用不可出现在各字段内容中的分隔符。
        var canonical = string.Join(
            FieldSeparator,
            sequence.ToString(CultureInfo.InvariantCulture),
            ((int)eventType).ToString(CultureInfo.InvariantCulture),
            occurredAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            subjectIdentifier,
            ((int)outcome).ToString(CultureInfo.InvariantCulture),
            previousHash);

        var bytes = Encoding.UTF8.GetBytes(canonical);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static string ComputePersistentHash(
        long sequence,
        SecurityAuditEventKind kind,
        DateTimeOffset occurredAt,
        string accountLabel,
        string detail,
        string previousHash)
    {
        var canonical = PersistentHashDomain + string.Join(
            FieldSeparator,
            sequence.ToString(CultureInfo.InvariantCulture),
            ((int)kind).ToString(CultureInfo.InvariantCulture),
            occurredAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            accountLabel,
            detail,
            previousHash);

        var bytes = Encoding.UTF8.GetBytes(canonical);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}

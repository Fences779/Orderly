using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Orderly.Core.Models;
using Orderly.Core.Services;

namespace Orderly.Data.Services;

/// <summary>
/// <see cref="ISecurityAuditService"/> 的后端实现（需求 2.4 / design Property 4）。
///
/// 防篡改思路：追加写 + 链式哈希。
/// - 每条记录的 <see cref="SecurityAuditRecord.PreviousHash"/> 指向上一条记录的
///   <see cref="SecurityAuditRecord.RecordHash"/>（首条指向创世哈希）。
/// - <see cref="SecurityAuditRecord.RecordHash"/> = SHA-256(规范化字段串 ‖ PreviousHash)。
/// - 任意历史记录被篡改/删除/重排都会破坏其后所有记录的哈希链，
///   可通过 <see cref="VerifyChainIntegrity"/> 检出。
///
/// 隐私：主体仅以单向哈希存储（<see cref="HashSubject"/>），不落明文凭证。
///
/// 线程安全：所有读写均在 <see cref="_syncRoot"/> 下进行，追加为原子操作。
/// </summary>
public sealed class SecurityAuditService : ISecurityAuditService
{
    // 创世哈希（64 个 '0'），作为首条记录的 PreviousHash。
    private const string GenesisHash = "0000000000000000000000000000000000000000000000000000000000000000";

    // 主体哈希的域分隔前缀，避免与其他哈希用途产生交叉。
    private const string SubjectHashDomain = "orderly.security-audit.subject.v1:";

    // 空主体的中性占位（仍以哈希形式存储，不暴露原因）。
    private const string AnonymousSubject = "<anonymous>";

    private readonly object _syncRoot = new();
    private readonly List<SecurityAuditRecord> _records = new();
    private string _lastHash = GenesisHash;
    private long _nextSequence;

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
            '\u001f',
            sequence.ToString(CultureInfo.InvariantCulture),
            ((int)eventType).ToString(CultureInfo.InvariantCulture),
            occurredAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            subjectIdentifier,
            ((int)outcome).ToString(CultureInfo.InvariantCulture),
            previousHash);

        var bytes = Encoding.UTF8.GetBytes(canonical);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}

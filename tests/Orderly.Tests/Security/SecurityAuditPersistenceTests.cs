using Microsoft.Data.Sqlite;
using Orderly.Core.Models;
using Orderly.Data.Services;
using Orderly.Data.Sqlite;
using Xunit;

namespace Orderly.Tests.Security;

/// <summary>
/// 任务 11.3 单元测试：<see cref="SecurityAuditService"/> 的防篡改加密存储写入与全量保留。
///
/// 验证 <c>RecordAsync</c> 将审计记录追加写入会话数据密钥加密的本地库（全库 SQLCipher 加密），
/// 仅存事件类型 / 时间 / 账号标签 / 脱敏 detail；以追加式 + 链式完整性哈希保持防篡改；完整保留全部历史，
/// 不截断或自动清除（需求 12.2、12.3、12.4、14.1、14.2、9.6）。
/// </summary>
public sealed class SecurityAuditPersistenceTests : IDisposable
{
    private readonly string _dir;
    private readonly string _dbPath;
    private readonly byte[] _key;

    public SecurityAuditPersistenceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "orderly-audit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _dbPath = Path.Combine(_dir, "audit.db");
        _key = Enumerable.Range(0, 32).Select(i => (byte)(i + 7)).ToArray();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private SqliteConnectionFactory Factory() => new(_dbPath, () => (byte[])_key.Clone());

    private SecurityAuditService NewService() => new(() => Factory());

    [Fact]
    public async Task RecordAsync_then_QueryAsync_round_trips_persisted_entries()
    {
        var service = NewService();

        await service.RecordAsync(SecurityAuditEventKind.LoginSucceeded, "owner", "登录成功");
        await service.RecordAsync(SecurityAuditEventKind.LoginFailed, "owner", "密码错误（脱敏）");

        // 用全新实例读取，确保走真实持久化往返而非内存残留。
        var reader = NewService();
        var entries = await reader.QueryAsync();

        Assert.Equal(2, entries.Count);
        Assert.Equal("LoginSucceeded", entries[0].Kind);
        Assert.Equal("owner", entries[0].AccountLabel);
        Assert.Equal("登录成功", entries[0].Detail);
        Assert.Equal("LoginFailed", entries[1].Kind);
        // 顺序稳定：按写入顺序（Sequence 升序）返回。
        Assert.True(entries[0].OccurredAt <= entries[1].OccurredAt);
    }

    [Fact]
    public async Task Persisted_chain_is_tamper_evident()
    {
        var service = NewService();
        await service.RecordAsync(SecurityAuditEventKind.LoginSucceeded, "owner", "a");
        await service.RecordAsync(SecurityAuditEventKind.CredentialChanged, "owner", "b");
        await service.RecordAsync(SecurityAuditEventKind.MemberCreated, "owner", "c");

        Assert.True(await service.VerifyPersistedChainIntegrityAsync());

        // 直接篡改一条历史记录的 Detail（绕过追加式写入），破坏其 RecordHash 覆盖范围。
        await using (var connection = Factory().CreateConnection())
        {
            await connection.OpenAsync();
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "UPDATE SecurityAuditEntries SET Detail = 'tampered' WHERE Sequence = 1;";
            await cmd.ExecuteNonQueryAsync();
        }
        SqliteConnection.ClearAllPools();

        Assert.False(await NewService().VerifyPersistedChainIntegrityAsync());
    }

    [Fact]
    public async Task Full_history_is_retained_without_truncation()
    {
        var service = NewService();
        const int count = 50;
        for (var i = 0; i < count; i++)
        {
            await service.RecordAsync(SecurityAuditEventKind.LoginSucceeded, "owner", $"事件 {i}");
        }

        var entries = await NewService().QueryAsync();
        Assert.Equal(count, entries.Count);
        Assert.True(await NewService().VerifyPersistedChainIntegrityAsync());
    }

    [Fact]
    public async Task QueryAsync_filters_by_account_label()
    {
        var service = NewService();
        await service.RecordAsync(SecurityAuditEventKind.LoginSucceeded, "owner", "x");
        await service.RecordAsync(SecurityAuditEventKind.LoginSucceeded, "member-1", "y");

        var ownerOnly = await NewService().QueryAsync(accountLabel: "owner");
        Assert.Single(ownerOnly);
        Assert.Equal("owner", ownerOnly[0].AccountLabel);
    }

    [Fact]
    public async Task RecordAsync_strips_control_characters_from_detail()
    {
        var service = NewService();
        await service.RecordAsync(SecurityAuditEventKind.LoginFailed, "owner\u001fspoof", "line1\r\nline2\u0000");

        var entries = await NewService().QueryAsync();
        Assert.Single(entries);
        Assert.Equal("ownerspoof", entries[0].AccountLabel);
        Assert.Equal("line1line2", entries[0].Detail);
    }

    [Fact]
    public async Task RecordAsync_is_best_effort_noop_without_encrypted_store()
    {
        // 无加密库连接来源（如未登录）：写入降级为无操作，读取返回空列表，绝不抛异常。
        var service = new SecurityAuditService();
        await service.RecordAsync(SecurityAuditEventKind.LoginSucceeded, "owner", "detail");

        var entries = await service.QueryAsync();
        Assert.Empty(entries);
    }

    [Fact]
    public async Task Persisted_entries_are_encrypted_at_rest()
    {
        var service = NewService();
        await service.RecordAsync(SecurityAuditEventKind.LoginSucceeded, "owner", "SENSITIVE_AUDIT_MARKER");
        SqliteConnection.ClearAllPools();

        var raw = await File.ReadAllBytesAsync(_dbPath);
        var text = System.Text.Encoding.ASCII.GetString(raw);
        Assert.DoesNotContain("SENSITIVE_AUDIT_MARKER", text);
        Assert.DoesNotContain("SQLite format 3", text);
    }
}

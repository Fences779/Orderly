using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CsCheck;
using Microsoft.Data.Sqlite;
using Orderly.Core.Models;
using Orderly.Data.Services;
using Orderly.Data.Sqlite;
using Orderly.Tests.Support;
using Xunit;

namespace Orderly.Tests.Security;

/// <summary>
/// 任务 11.6 属性测试：<see cref="SecurityAuditService"/> 安全审计完整性与降级
/// （design §11 Property 9）。
///
/// <para><b>Property 9: 安全审计完整性与降级.</b>
/// 每个认证 / 账户安全敏感操作（登录成功 / 失败、账户锁定、凭证变更、成员创建 / 重置 / 停用 / 删除）
/// 恰好产生一条对应类型的审计记录，存于加密本地存储且记录中不含明文凭证；当查询无记录时呈现空状态
/// 而非臆造或半截数据。本测试以三组互补的性质刻画该不变量：
/// <list type="bullet">
///   <item><b>完整性</b>：写入任意审计记录序列后 <c>VerifyPersistedChainIntegrityAsync</c> 恒为
///   <c>true</c>；任意单条历史记录被篡改（改写其字段）或被删除（破坏序号连续性）后恒变
///   <c>false</c>，即防篡改追加式链可检出任何历史改动。</item>
///   <item><b>恰好一条 + 数量一致</b>：每次 <c>RecordAsync</c> 恰好追加一条记录（计数严格 +1），
///   且 <c>QueryAsync</c> 读回的记录数恒等于写入数（不丢、不重、不臆造）。</item>
///   <item><b>降级</b>：无加密库连接来源时 <c>RecordAsync</c> 尽力而为（不抛异常、降级为无操作），
///   <c>QueryAsync</c> 返回空列表（呈现空状态而非半截 / 臆造数据）。</item>
/// </list></para>
///
/// <para>沿用 <see cref="SecurityAuditPersistenceTests"/> 的临时 SQLCipher 加密库构造
/// （会话数据密钥全库加密、真实持久化往返、无 mock）；每次迭代使用全新临时库，并在
/// <c>finally</c> 中清理。</para>
///
/// **Validates: Requirements 12.2, 12.1, 12.3, 12.5, 9.3, 14.2**
/// </summary>
public sealed class SecurityAuditIntegrityDegradationPropertyTests
{
    private static readonly Gen<SecurityAuditEventKind> KindGen =
        Gen.OneOfConst(Enum.GetValues<SecurityAuditEventKind>());

    // 干净的账号标签集合（无控制字符 / 无首尾空白），覆盖中英文，规避规范化对内容的改写干扰。
    private static readonly Gen<string> LabelGen =
        Gen.OneOfConst("owner", "member-1", "member-2", "管理员", "店员");

    // 脱敏 detail：小写字母串，便于与篡改用的大写哨兵值区分（保证篡改一定改变内容）。
    private static readonly Gen<string> DetailGen =
        Gen.Char['a', 'z'].Array[1, 10].Select(chars => new string(chars));

    private static readonly Gen<(SecurityAuditEventKind Kind, string Label, string Detail)> EntryGen =
        from kind in KindGen
        from label in LabelGen
        from detail in DetailGen
        select (kind, label, detail);

    // 篡改字段选择：0 = Detail，1 = AccountLabel，2 = Kind。
    private static readonly Gen<(List<(SecurityAuditEventKind Kind, string Label, string Detail)> Entries, int TargetPermille, int FieldSelector)> TamperScenarioGen =
        from entries in EntryGen.List[1, 8]
        from targetPermille in Gen.Int[0, 1000]
        from fieldSelector in Gen.Int[0, 2]
        select (entries, targetPermille, fieldSelector);

    // 删除场景：至少 2 条，删除的目标限定为「非末条」历史记录，删除后序号必现空洞 → 链断裂可检出。
    private static readonly Gen<(List<(SecurityAuditEventKind Kind, string Label, string Detail)> Entries, int TargetPermille)> DeleteScenarioGen =
        from entries in EntryGen.List[2, 8]
        from targetPermille in Gen.Int[0, 1000]
        select (entries, targetPermille);

    [Fact]
    public void Property9_intact_chain_verifies_and_any_tampered_record_is_detected()
    {
        TamperScenarioGen.Sample(
            scenario =>
            {
                WithTempStore((factory, newService) =>
                {
                    var writer = newService();
                    foreach (var (kind, label, detail) in scenario.Entries)
                    {
                        writer.RecordAsync(kind, label, detail).GetAwaiter().GetResult();
                    }

                    int written = scenario.Entries.Count;

                    // ── 完整性：未被篡改的链恒校验通过。 ──
                    Assert.True(newService().VerifyPersistedChainIntegrityAsync().GetAwaiter().GetResult());

                    // 选取被篡改的目标记录（序号 0..written-1）。
                    long targetSequence = MapPermilleToIndex(scenario.TargetPermille, written);

                    // 就地改写目标记录的某个被哈希覆盖的字段为一个保证不同的值（破坏 RecordHash 覆盖范围）。
                    using (var connection = factory().CreateConnection())
                    {
                        connection.Open();
                        using var cmd = connection.CreateCommand();
                        cmd.CommandText = scenario.FieldSelector switch
                        {
                            0 => "UPDATE SecurityAuditEntries SET Detail = 'TAMPERED_SENTINEL' WHERE Sequence = $seq;",
                            1 => "UPDATE SecurityAuditEntries SET AccountLabel = 'TAMPERED_LABEL' WHERE Sequence = $seq;",
                            _ => "UPDATE SecurityAuditEntries SET Kind = Kind + 1000 WHERE Sequence = $seq;",
                        };
                        cmd.Parameters.AddWithValue("$seq", targetSequence);
                        cmd.ExecuteNonQuery();
                    }

                    SqliteConnection.ClearAllPools();

                    // ── 完整性：任意单条历史被篡改后校验必失败。 ──
                    Assert.False(newService().VerifyPersistedChainIntegrityAsync().GetAwaiter().GetResult());
                });
            },
            iter: PbtConfig.MinIterations);
    }

    [Fact]
    public void Property9_deleting_any_historical_record_breaks_chain_integrity()
    {
        DeleteScenarioGen.Sample(
            scenario =>
            {
                WithTempStore((factory, newService) =>
                {
                    var writer = newService();
                    foreach (var (kind, label, detail) in scenario.Entries)
                    {
                        writer.RecordAsync(kind, label, detail).GetAwaiter().GetResult();
                    }

                    int written = scenario.Entries.Count;
                    Assert.True(newService().VerifyPersistedChainIntegrityAsync().GetAwaiter().GetResult());

                    // 删除一条「非末条」历史记录（序号 0..written-2），使序号出现空洞 → 链断裂可检出。
                    long targetSequence = MapPermilleToIndex(scenario.TargetPermille, written - 1);

                    using (var connection = factory().CreateConnection())
                    {
                        connection.Open();
                        using var cmd = connection.CreateCommand();
                        cmd.CommandText = "DELETE FROM SecurityAuditEntries WHERE Sequence = $seq;";
                        cmd.Parameters.AddWithValue("$seq", targetSequence);
                        cmd.ExecuteNonQuery();
                    }

                    SqliteConnection.ClearAllPools();

                    Assert.False(newService().VerifyPersistedChainIntegrityAsync().GetAwaiter().GetResult());
                });
            },
            iter: PbtConfig.MinIterations);
    }

    [Fact]
    public void Property9_each_record_appends_exactly_one_and_query_count_matches_writes()
    {
        EntryGen.List[1, 8].Sample(
            entries =>
            {
                WithTempStore((_, newService) =>
                {
                    var service = newService();
                    int expected = 0;

                    foreach (var (kind, label, detail) in entries)
                    {
                        service.RecordAsync(kind, label, detail).GetAwaiter().GetResult();
                        expected++;

                        // ── 恰好一条：每次写入后总数严格 +1（不丢、不重）。 ──
                        var afterWrite = newService().QueryAsync().GetAwaiter().GetResult();
                        Assert.Equal(expected, afterWrite.Count);
                    }

                    // ── 数量一致：读回数量恒等于写入数。 ──
                    var all = newService().QueryAsync().GetAwaiter().GetResult();
                    Assert.Equal(entries.Count, all.Count);

                    // 读回内容与写入逐条对应（顺序稳定、不臆造），同时链保持完整。
                    for (int i = 0; i < entries.Count; i++)
                    {
                        Assert.Equal(entries[i].Kind.ToString(), all[i].Kind);
                        Assert.Equal(entries[i].Label, all[i].AccountLabel);
                        Assert.Equal(entries[i].Detail, all[i].Detail);
                    }

                    Assert.True(newService().VerifyPersistedChainIntegrityAsync().GetAwaiter().GetResult());
                });
            },
            iter: PbtConfig.MinIterations);
    }

    [Fact]
    public void Property9_without_encrypted_store_record_is_noop_and_query_is_empty()
    {
        EntryGen.List[0, 8].Sample(
            entries =>
            {
                // 两种「无加密库连接」形态：1) 无 provider 的构造；2) provider 返回 null。
                var noProvider = new SecurityAuditService();
                var nullProvider = new SecurityAuditService(() => null);

                foreach (var service in new[] { noProvider, nullProvider })
                {
                    foreach (var (kind, label, detail) in entries)
                    {
                        // ── 降级：尽力而为，绝不抛异常。 ──
                        var ex = Record.Exception(() => service.RecordAsync(kind, label, detail).GetAwaiter().GetResult());
                        Assert.Null(ex);
                    }

                    // ── 降级：查询返回空列表（呈现空状态而非臆造 / 半截数据）。 ──
                    var queried = service.QueryAsync().GetAwaiter().GetResult();
                    Assert.Empty(queried);

                    // 无存储时完整性自检视为完整（返回 true），不抛异常。
                    Assert.True(service.VerifyPersistedChainIntegrityAsync().GetAwaiter().GetResult());
                }
            },
            iter: PbtConfig.MinIterations);
    }

    /// <summary>
    /// 在全新临时 SQLCipher 加密库上执行 <paramref name="body"/>，并在结束后清理连接池与临时目录。
    /// 提供连接工厂（用于直接篡改 / 校验）与「新建服务实例」委托（用于真实持久化往返读写）。
    /// </summary>
    private static void WithTempStore(Action<Func<SqliteConnectionFactory>, Func<SecurityAuditService>> body)
    {
        var dir = Path.Combine(Path.GetTempPath(), "orderly-audit-p9-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var dbPath = Path.Combine(dir, "audit.db");
        var key = Enumerable.Range(0, 32).Select(i => (byte)(i + 7)).ToArray();

        SqliteConnectionFactory Factory() => new(dbPath, () => (byte[])key.Clone());
        SecurityAuditService NewService() => new(() => Factory());

        try
        {
            body(Factory, NewService);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }

    // 将 [0,1000] 的分位指针映射为 [0, count-1] 的下标（count >= 1）。
    private static int MapPermilleToIndex(int permille, int count)
    {
        int index = (int)((long)permille * count / 1001L);
        if (index >= count)
        {
            index = count - 1;
        }

        return index < 0 ? 0 : index;
    }
}

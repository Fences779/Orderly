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
/// 任务 11.7 属性测试：<see cref="SecurityAuditService"/> 审计按日期筛选与全保留
/// （design §6.4 / §11 Property 15）。
///
/// <para><b>Property 15: 审计按日期筛选与全保留.</b>
/// 对任意写入的审计记录序列与任意时间范围 <c>[from, to]</c>（含边界，null 表示该侧不限）：
/// <list type="bullet">
///   <item><b>日期范围子集</b>：<c>QueryAsync(from, to)</c> 恰好返回 <c>OccurredAt</c> 落在
///   <c>[from, to]</c>（含边界）内的记录子集，绝不包含范围外记录、绝不遗漏范围内记录。</item>
///   <item><b>全量保留</b>：无界查询 <c>QueryAsync()</c> 的记录总数恒等于写入数，不因过滤查询
///   被截断或自动清除而减少。</item>
///   <item><b>顺序稳定</b>：相同范围多次查询返回完全一致的结果，且按 <c>OccurredAt</c> 升序
///   （写入顺序 / Sequence 升序）。</item>
///   <item><b>边界不限</b>：<c>from = null</c> 仅施加上界约束、<c>to = null</c> 仅施加下界约束、
///   两者皆 null 等价于全量查询。</item>
/// </list></para>
///
/// <para>记录的 <c>OccurredAt</c> 由服务以 <c>UtcNow</c> 在写入时生成，测试无法直接指定。
/// 因此先以无界查询拿到全部记录及其 <c>OccurredAt</c>，再以其中的实际时间值（由生成的分位
/// 指针选取）作为 <c>from</c> / <c>to</c>，并独立地从全量列表过滤出期望子集与读取结果比对，
/// 避免循环论证。沿用 <see cref="SecurityAuditPersistenceTests"/> 的临时 SQLCipher 加密库构造
/// （会话数据密钥全库加密、真实持久化往返、无 mock）；每次迭代使用全新临时库，并在
/// <c>finally</c> 中清理。</para>
///
/// **Validates: Requirements 9.6, 9.7, 12.5**
/// </summary>
public sealed class SecurityAuditDateRangeRetentionPropertyTests
{
    private static readonly Gen<SecurityAuditEventKind> KindGen =
        Gen.OneOfConst(Enum.GetValues<SecurityAuditEventKind>());

    // 干净的账号标签集合（无控制字符 / 无首尾空白），覆盖中英文，规避规范化对内容的改写干扰。
    private static readonly Gen<string> LabelGen =
        Gen.OneOfConst("owner", "member-1", "member-2", "管理员", "店员");

    private static readonly Gen<string> DetailGen =
        Gen.Char['a', 'z'].Array[1, 10].Select(chars => new string(chars));

    private static readonly Gen<(SecurityAuditEventKind Kind, string Label, string Detail)> EntryGen =
        from kind in KindGen
        from label in LabelGen
        from detail in DetailGen
        select (kind, label, detail);

    private static readonly Gen<(List<(SecurityAuditEventKind Kind, string Label, string Detail)> Entries, int FromPermille, int ToPermille)> ScenarioGen =
        from entries in EntryGen.List[1, 10]
        from fromPermille in Gen.Int[0, 1000]
        from toPermille in Gen.Int[0, 1000]
        select (entries, fromPermille, toPermille);

    [Fact]
    public void Property15_date_range_query_returns_inrange_subset_and_full_history_is_retained()
    {
        ScenarioGen.Sample(
            scenario =>
            {
                var dir = Path.Combine(Path.GetTempPath(), "orderly-audit-p15-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(dir);
                var dbPath = Path.Combine(dir, "audit.db");
                var key = Enumerable.Range(0, 32).Select(i => (byte)(i + 7)).ToArray();

                SqliteConnectionFactory Factory() => new(dbPath, () => (byte[])key.Clone());
                SecurityAuditService NewService() => new(() => Factory());

                try
                {
                    // 写入生成的审计记录序列（含 MemberDeleted 等全部事件类型）。
                    var writer = NewService();
                    foreach (var (kind, label, detail) in scenario.Entries)
                    {
                        writer.RecordAsync(kind, label, detail).GetAwaiter().GetResult();
                    }

                    int written = scenario.Entries.Count;

                    // 用全新实例读取，确保走真实持久化往返而非内存残留。
                    var reader = NewService();

                    // ── 全量保留：无界查询总数等于写入数，不因过滤查询而减少。 ──
                    var all = reader.QueryAsync().GetAwaiter().GetResult();
                    Assert.Equal(written, all.Count);

                    // ── 顺序稳定：无界查询按 OccurredAt 升序（写入顺序 / Sequence 升序）。 ──
                    for (int i = 1; i < all.Count; i++)
                    {
                        Assert.True(all[i - 1].OccurredAt <= all[i].OccurredAt);
                    }

                    // 用全量记录的真实 OccurredAt 选取范围边界（含边界），由生成分位指针定位。
                    var times = all.Select(e => e.OccurredAt).ToList();
                    int lo = MapPermilleToIndex(scenario.FromPermille, times.Count);
                    int hi = MapPermilleToIndex(scenario.ToPermille, times.Count);
                    if (lo > hi)
                    {
                        (lo, hi) = (hi, lo);
                    }

                    DateTime from = times[lo];
                    DateTime to = times[hi];

                    // ── 日期范围子集：范围查询恰为 OccurredAt ∈ [from, to] 的子集，顺序与全量一致。 ──
                    var expected = all.Where(e => e.OccurredAt >= from && e.OccurredAt <= to).ToList();
                    var ranged = reader.QueryAsync(from: from, to: to).GetAwaiter().GetResult();
                    AssertEntriesEqual(expected, ranged);

                    // 子集关系：范围查询结果不超过全量。
                    Assert.True(ranged.Count <= all.Count);

                    // ── 顺序稳定：相同范围多次查询结果完全一致。 ──
                    var rangedAgain = reader.QueryAsync(from: from, to: to).GetAwaiter().GetResult();
                    AssertEntriesEqual(ranged, rangedAgain);

                    // ── 边界：from = null 仅施加上界约束。 ──
                    var upperOnly = reader.QueryAsync(from: null, to: to).GetAwaiter().GetResult();
                    AssertEntriesEqual(all.Where(e => e.OccurredAt <= to).ToList(), upperOnly);

                    // ── 边界：to = null 仅施加下界约束。 ──
                    var lowerOnly = reader.QueryAsync(from: from, to: null).GetAwaiter().GetResult();
                    AssertEntriesEqual(all.Where(e => e.OccurredAt >= from).ToList(), lowerOnly);

                    // ── 边界：from / to 皆 null 等价于全量查询。 ──
                    var unbounded = reader.QueryAsync(from: null, to: null).GetAwaiter().GetResult();
                    AssertEntriesEqual(all, unbounded);
                }
                finally
                {
                    SqliteConnection.ClearAllPools();
                    try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
                }
            },
            iter: PbtConfig.MinIterations);
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

    private static void AssertEntriesEqual(
        IReadOnlyList<SecurityAuditEntry> expected,
        IReadOnlyList<SecurityAuditEntry> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (int i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].OccurredAt, actual[i].OccurredAt);
            Assert.Equal(expected[i].Kind, actual[i].Kind);
            Assert.Equal(expected[i].AccountLabel, actual[i].AccountLabel);
            Assert.Equal(expected[i].Detail, actual[i].Detail);
        }
    }
}

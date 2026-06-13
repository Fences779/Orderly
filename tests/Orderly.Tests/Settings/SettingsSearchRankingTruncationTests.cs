using System;
using System.Collections.Generic;
using System.Linq;
using Orderly.App.ViewModels;
using Xunit;

namespace Orderly.Tests.Settings;

/// <summary>
/// 明确示例单元测试：验证 <see cref="SettingsSearchIndex"/> 的过滤 / 排序 / 截断算法
/// （设计 §9.4 / 任务 6.2 / 6.5）。
///
/// <para>覆盖三类确定性行为：</para>
/// <list type="bullet">
/// <item><b>命中权重排序</b>（Req 2.1）：标题前缀(100) &gt; 标题子串(60) &gt; 描述(25) &gt; 关键字(20)，
/// 命中权重降序、同分按 <c>CategoryKey</c> 序数（Ordinal）稳定排序。</item>
/// <item><b>结果上限与截断标志</b>（Req 2.6 / 2.8）：原始命中超过 <see cref="SettingsSearchIndex.MaxResults"/>（=12）
/// 时恰好返回 12 条且 <c>IsTruncated=true</c>；未超限时返回全部且 <c>IsTruncated=false</c>。</item>
/// <item><b>大小写不敏感</b>（Req 2.1）：仅大小写不同的查询返回完全一致的有序结果。</item>
/// </list>
///
/// **Validates: Requirements 2.1, 2.6, 2.8**
/// </summary>
public sealed class SettingsSearchRankingTruncationTests
{
    private static readonly SettingsSearchIndex Index = new();

    /// <summary>
    /// 排序：查询「备份」命中 8 条，按权重清晰分层——
    /// 标题前缀命中（备份保留数量 / 备份目录，145 分）排在标题子串命中（自动备份等，105 分）之前；
    /// 标题子串命中又排在「无标题命中、仅描述+关键字命中」（执行安全恢复，45 分）之前。
    /// 直接体现 标题前缀 &gt; 标题子串 &gt; 描述/关键字 的命中权重序（Req 2.1）。
    /// </summary>
    [Fact]
    public void Search_orders_title_prefix_above_substring_above_non_title_hits()
    {
        SettingsSearchResult result = Index.Search("备份");

        // 命中 8 条（≤ 12），不触发截断。
        Assert.Equal(8, result.Entries.Count);
        Assert.False(result.IsTruncated);

        var titles = result.Entries.Select(e => e.Title).ToList();

        // 前两位为标题前缀命中（145 分），按同分同分类的静态稳定顺序：备份保留数量 → 备份目录。
        Assert.Equal("备份保留数量", titles[0]);
        Assert.Equal("备份目录", titles[1]);

        // 末位为「仅描述+关键字命中、标题未命中」的最低分条目（45 分）。
        Assert.Equal("执行安全恢复", titles[^1]);

        // 标题前缀命中严格排在标题子串命中（自动备份，105 分）之前。
        Assert.True(
            titles.IndexOf("备份保留数量") < titles.IndexOf("自动备份"),
            "标题前缀命中应排在标题子串命中之前");

        // 标题子串命中（105 分）严格排在仅描述+关键字命中（45 分）之前。
        Assert.True(
            titles.IndexOf("自动备份") < titles.IndexOf("执行安全恢复"),
            "标题子串命中应排在仅描述/关键字命中之前");
    }

    /// <summary>
    /// 排序（关键字命中权重最低）：查询「脱敏」命中 5 条，其中「导出包含敏感信息」
    /// 仅经关键字命中（标题与描述均无「脱敏」字样，20 分），其余 4 条均含标题子串命中（105 分）。
    /// 验证仅关键字命中条目排在所有标题命中条目之后——关键字为最低命中权重层（Req 2.1）。
    /// 同时验证同分（105）时按 <c>CategoryKey</c> 序数稳定排序：「AI 助手」先于「安全与日志」。
    /// </summary>
    [Fact]
    public void Search_ranks_keyword_only_hit_last_and_breaks_ties_by_category()
    {
        SettingsSearchResult result = Index.Search("脱敏");

        Assert.Equal(5, result.Entries.Count);
        Assert.False(result.IsTruncated);

        var titles = result.Entries.Select(e => e.Title).ToList();

        // 仅关键字命中的条目（20 分）排在最后，低于所有标题子串命中（105 分）。
        SettingsSearchEntry keywordOnly = result.Entries[^1];
        Assert.Equal("导出包含敏感信息", keywordOnly.Title);
        Assert.DoesNotContain("脱敏", keywordOnly.Title);
        Assert.DoesNotContain("脱敏", keywordOnly.Description);
        Assert.Contains(keywordOnly.Keywords, k => k.Contains("脱敏", StringComparison.Ordinal));

        // 同分（105）时按分类序数稳定排序：「AI 助手」(U+0041 起) 先于「安全与日志」(U+5B89 起)。
        Assert.Equal("发送前自动脱敏", titles[0]);
        Assert.True(
            titles.IndexOf("发送前自动脱敏") < titles.IndexOf("默认脱敏手机号"),
            "同分时『AI 助手』分类应按序数排在『安全与日志』之前");
    }

    /// <summary>
    /// 截断：构造高频命中查询「a」（命中 accent / startup / backup 等关键字与全部 12 条
    /// AI 条目，原始命中 &gt; 12）→ 结果恰好截断为 <see cref="SettingsSearchIndex.MaxResults"/>(12) 条，
    /// 且「结果超限」标志 <c>IsTruncated</c> 置位（Req 2.6 / 2.8）。
    /// </summary>
    [Fact]
    public void Search_truncates_to_max_results_and_sets_flag_when_over_limit()
    {
        SettingsSearchResult result = Index.Search("a");

        Assert.Equal(SettingsSearchIndex.MaxResults, result.Entries.Count);
        Assert.Equal(12, result.Entries.Count);
        Assert.True(result.IsTruncated, "原始命中超过 12 条时应置位 IsTruncated");

        // Query 是 Search().Entries 的别名，同样被截断为 12 条。
        Assert.Equal(SettingsSearchIndex.MaxResults, Index.Query("a").Count);
    }

    /// <summary>
    /// 上限边界：查询「AI」恰好命中 12 条（六大分类中的全部 AI 条目），等于上限但未超过，
    /// 因此返回全部 12 条且 <c>IsTruncated=false</c>——验证截断仅在「严格超过」上限时触发。
    /// </summary>
    [Fact]
    public void Search_at_exactly_max_results_is_not_truncated()
    {
        SettingsSearchResult result = Index.Search("AI");

        Assert.Equal(SettingsSearchIndex.MaxResults, result.Entries.Count);
        Assert.False(result.IsTruncated, "命中数恰好等于上限（12）时不应置位 IsTruncated");
    }

    /// <summary>
    /// 大小写不敏感：仅大小写不同的查询返回完全一致的有序结果与一致的截断标志（Req 2.1）。
    /// 覆盖「恰好 12 条不截断」（AI/ai/Ai/aI）与「超限截断 12 条」（a/A）两种分支。
    /// </summary>
    [Theory]
    [InlineData("AI", "ai")]
    [InlineData("AI", "Ai")]
    [InlineData("ai", "aI")]
    [InlineData("a", "A")]
    public void Search_is_case_insensitive(string upper, string lower)
    {
        SettingsSearchResult upperResult = Index.Search(upper);
        SettingsSearchResult lowerResult = Index.Search(lower);

        Assert.Equal(upperResult.IsTruncated, lowerResult.IsTruncated);
        AssertSameOrderedEntries(upper, lower, upperResult.Entries, lowerResult.Entries);
    }

    /// <summary>断言两个命中序列逐元素完全相等（同一顺序、同一条目身份）。</summary>
    private static void AssertSameOrderedEntries(
        string left,
        string right,
        IReadOnlyList<SettingsSearchEntry> leftEntries,
        IReadOnlyList<SettingsSearchEntry> rightEntries)
    {
        Assert.True(
            leftEntries.Count == rightEntries.Count,
            $"查询 '{left}' 与 '{right}' 结果条数不同：{leftEntries.Count} vs {rightEntries.Count}");

        for (var i = 0; i < leftEntries.Count; i++)
        {
            Assert.True(
                leftEntries[i] == rightEntries[i],
                $"查询 '{left}' 与 '{right}' 第 {i} 位结果不一致：'{leftEntries[i].Title}' vs '{rightEntries[i].Title}'");
        }
    }
}

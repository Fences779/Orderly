using System;
using System.Collections.Generic;
using System.Linq;
using CsCheck;
using Orderly.App.ViewModels;
using Orderly.Tests.Support;
using Xunit;

namespace Orderly.Tests.Settings;

/// <summary>
/// Property-based test for <see cref="SettingsSearchIndex"/> search category closure
/// (design §9.4 / §11 Property 3).
///
/// <para><b>Property 3: 搜索分类闭合.</b>
/// 对任意查询串 <c>q</c>，<see cref="SettingsSearchIndex.Search"/> / <see cref="SettingsSearchIndex.Query"/>
/// 返回的每一条命中项都满足：其所属分类 <c>CategoryKey</c> 必属于六大分类之一
/// （<see cref="SettingsSearchIndex.CategoryKeys"/>，Req 2.4），其滚动定位锚点 <c>AnchorId</c> 非空
/// （Req 2.4），且返回结果数量不超过上限 <see cref="SettingsSearchIndex.MaxResults"/>（=12，Req 2.6）。</para>
///
/// <para>该属性是闭合性 / 不变式属性：无论查询串命中多少条、是否触发截断、是否为空/空白查询，
/// 结果集合都恒定落在「六大分类 × 非空锚点」的合法值域内，且基数受 <c>MaxResults</c> 约束。</para>
///
/// **Validates: Requirements 2.4, 2.6**
/// </summary>
public sealed class SettingsSearchCategoryClosurePropertyTests
{
    private static readonly SettingsSearchIndex Index = new();

    // 六大分类 key 集合（值域闭包），用于断言每条命中项的 CategoryKey 落在其中。
    private static readonly HashSet<string> AllowedCategories =
        new(SettingsSearchIndex.CategoryKeys, StringComparer.Ordinal);

    // 从静态索引中抽取的「有意义词元」语料：标题、标题切片与关键字。以这些词元为种子生成查询串，
    // 能可靠命中真实条目（含触发 12 条截断的高频词如「备份」「AI」「快捷键」「通知」），
    // 从而让属性覆盖到「有命中 / 大量命中被截断」的关键分支，而不仅是空命中。
    private static readonly string[] Corpus = BuildCorpus();

    // 任意字符生成器：覆盖拉丁字母、数字、常见符号、空白与中文区段，触及空命中 / 空白查询等分支。
    private static readonly Gen<char> CharGen = Gen.OneOf(
        Gen.Char['a', 'z'],
        Gen.Char['A', 'Z'],
        Gen.Char['0', '9'],
        Gen.OneOfConst(' ', '\t', '/', '-', '_', '@', '#'),
        Gen.Char['\u4e00', '\u9fa5']);

    // 任意查询串：长度 0..12，覆盖空串 / 空白串 / 随机串。
    private static readonly Gen<string> ArbitraryQueryGen =
        CharGen.Array[0, 12].Select(chars => new string(chars));

    // 语料词元查询串：直接取真实词元，保证产生命中（含高频词触发截断）。
    private static readonly Gen<string> CorpusQueryGen =
        Gen.Int[0, Corpus.Length - 1].Select(i => Corpus[i]);

    // 混合查询生成器：偏向语料词元（产生命中、覆盖截断），同时保留任意串覆盖空命中边界。
    private static readonly Gen<string> QueryGen = Gen.Frequency(
        (3, CorpusQueryGen),
        (1, ArbitraryQueryGen));

    [Fact]
    public void Property3_every_result_has_valid_category_nonempty_anchor_and_bounded_count()
    {
        QueryGen.Sample(
            query =>
            {
                SettingsSearchResult result = Index.Search(query);
                IReadOnlyList<SettingsSearchEntry> entries = result.Entries;

                // 基数闭合（Req 2.6）：结果数不超过 MaxResults(12)。
                Assert.True(
                    entries.Count <= SettingsSearchIndex.MaxResults,
                    $"查询 '{query}' 返回 {entries.Count} 条，超过上限 {SettingsSearchIndex.MaxResults}");

                // 截断标志与基数自洽：仅当原始命中超限时才截断，截断时恰好返回 MaxResults 条。
                if (result.IsTruncated)
                {
                    Assert.Equal(SettingsSearchIndex.MaxResults, entries.Count);
                }

                foreach (SettingsSearchEntry entry in entries)
                {
                    // 分类闭合（Req 2.4）：CategoryKey 必属六大分类之一。
                    Assert.True(
                        AllowedCategories.Contains(entry.CategoryKey),
                        $"查询 '{query}' 命中项分类 '{entry.CategoryKey}' 不属于六大分类");

                    // 锚点非空（Req 2.4）：AnchorId 非 null 且非空白。
                    Assert.False(
                        string.IsNullOrWhiteSpace(entry.AnchorId),
                        $"查询 '{query}' 命中项 '{entry.Title}' 的 AnchorId 为空");
                }

                // Query 是 Search().Entries 的别名，闭合性同样成立。
                IReadOnlyList<SettingsSearchEntry> queryEntries = Index.Query(query);
                Assert.True(queryEntries.Count <= SettingsSearchIndex.MaxResults);
                Assert.All(queryEntries, e =>
                {
                    Assert.Contains(e.CategoryKey, AllowedCategories);
                    Assert.False(string.IsNullOrWhiteSpace(e.AnchorId));
                });
            },
            iter: PbtConfig.MinIterations);
    }

    /// <summary>
    /// 从静态索引条目派生查询语料：每条标题、标题前缀切片，以及全部关键字。去重后作为种子。
    /// </summary>
    private static string[] BuildCorpus()
    {
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        foreach (SettingsSearchEntry entry in Index.Entries)
        {
            tokens.Add(entry.Title);
            if (entry.Title.Length >= 2)
            {
                tokens.Add(entry.Title.Substring(0, 2));
            }

            foreach (string keyword in entry.Keywords)
            {
                if (!string.IsNullOrWhiteSpace(keyword))
                {
                    tokens.Add(keyword);
                }
            }
        }

        return tokens.ToArray();
    }
}

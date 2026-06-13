using System;
using System.Collections.Generic;
using System.Linq;
using CsCheck;
using Orderly.App.ViewModels;
using Orderly.Tests.Support;
using Xunit;

namespace Orderly.Tests.Settings;

/// <summary>
/// Property-based test for <see cref="SettingsSearchIndex"/> search determinism
/// (design §9.4 / §11 Property 4).
///
/// <para><b>Property 4: 搜索确定性.</b>
/// 对任意查询串 <c>q</c>，多次调用 <see cref="SettingsSearchIndex.Search"/> /
/// <see cref="SettingsSearchIndex.Query"/> 返回顺序一致的相同结果（同一查询多次调用的结果序列
/// 完全相等，含条目顺序、<c>IsTruncated</c> 标志、以及 <c>Query</c> 与 <c>Search().Entries</c> 的一致性，
/// Req 2.5 / 2.1）；空 / 仅空白字符的查询恒返回空结果列表（Req 2.2）。</para>
///
/// <para>该属性是确定性 / 幂等性不变式：相同输入在任意次数、任意调用入口下都产出逐元素相等的有序结果，
/// 不依赖隐藏状态或调用次序；空 / 空白查询是该确定性的恒定特例（恒为空）。</para>
///
/// **Validates: Requirements 2.5, 2.2, 2.1**
/// </summary>
public sealed class SettingsSearchDeterminismPropertyTests
{
    private static readonly SettingsSearchIndex Index = new();

    // 从静态索引派生「有意义词元」语料：标题、标题前缀切片与关键字。以真实词元为种子可靠命中条目，
    // 覆盖「有命中 / 高频词触发 12 条截断」分支，使确定性断言落在真实有序结果而非仅空命中上。
    private static readonly string[] Corpus = BuildCorpus();

    // 任意字符生成器：覆盖拉丁字母、数字、符号、空白与中文区段，触及空命中 / 空白查询等边界。
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

    // 混合查询生成器：偏向语料词元（覆盖有序命中 / 截断），同时保留任意串覆盖空命中边界。
    private static readonly Gen<string> QueryGen = Gen.Frequency(
        (3, CorpusQueryGen),
        (1, ArbitraryQueryGen));

    // 仅空白字符生成器：空串与各类空白组合，断言恒返回空结果（Req 2.2）。
    private static readonly Gen<string> BlankQueryGen =
        Gen.OneOfConst(' ', '\t', '\n', '\r', '\f', '\v').Array[0, 8]
            .Select(chars => new string(chars));

    [Fact]
    public void Property4_same_query_returns_identical_ordered_results_across_repeated_calls()
    {
        QueryGen.Sample(
            query =>
            {
                // 多次调用 Search：结果序列须逐元素完全相等（顺序 + 引用同一身份 + 截断标志一致）。
                SettingsSearchResult first = Index.Search(query);
                SettingsSearchResult second = Index.Search(query);
                SettingsSearchResult third = Index.Search(query);

                AssertSameOrderedEntries(query, first.Entries, second.Entries);
                AssertSameOrderedEntries(query, second.Entries, third.Entries);

                Assert.Equal(first.IsTruncated, second.IsTruncated);
                Assert.Equal(second.IsTruncated, third.IsTruncated);

                // Query 是 Search().Entries 的别名：多次调用同样确定，且与 Search 结果序列一致（Req 2.1）。
                IReadOnlyList<SettingsSearchEntry> queryFirst = Index.Query(query);
                IReadOnlyList<SettingsSearchEntry> querySecond = Index.Query(query);
                AssertSameOrderedEntries(query, queryFirst, querySecond);
                AssertSameOrderedEntries(query, first.Entries, queryFirst);
            },
            iter: PbtConfig.MinIterations);
    }

    [Fact]
    public void Property4_blank_or_whitespace_query_always_returns_empty()
    {
        BlankQueryGen.Sample(
            query =>
            {
                // 空 / 仅空白查询恒返回空结果列表且不截断（Req 2.2）。
                SettingsSearchResult result = Index.Search(query);
                Assert.Empty(result.Entries);
                Assert.False(result.IsTruncated, $"空白查询 '{query}' 不应触发截断");

                Assert.Empty(Index.Query(query));

                // null 同样按空白处理，恒返回空。
                Assert.Empty(Index.Search(null!).Entries);
                Assert.Empty(Index.Query(null!));
            },
            iter: PbtConfig.MinIterations);
    }

    /// <summary>断言两个命中序列逐元素完全相等（同一顺序、同一条目身份）。</summary>
    private static void AssertSameOrderedEntries(
        string query,
        IReadOnlyList<SettingsSearchEntry> left,
        IReadOnlyList<SettingsSearchEntry> right)
    {
        Assert.True(
            left.Count == right.Count,
            $"查询 '{query}' 两次调用结果条数不同：{left.Count} vs {right.Count}");

        for (var i = 0; i < left.Count; i++)
        {
            // 记录为 record，按值相等即可判定「相同结果」；同时校验顺序位置一致。
            Assert.True(
                left[i] == right[i],
                $"查询 '{query}' 第 {i} 位结果不一致：'{left[i].Title}' vs '{right[i].Title}'");
        }
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

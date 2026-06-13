using System.Collections.Generic;

namespace Orderly.App.ViewModels;

/// <summary>
/// 设置项可搜索条目（设计文档 §9.4 / Req 2.4）。
/// 每条登记标题、描述、关键字、所属分类与稳定锚点 AutomationId，供设置搜索命中后
/// 切换分类并滚动定位高亮使用。条目为静态文案，随 UI 文案变更需同步维护。
/// </summary>
/// <param name="Title">行项标题（中文）。</param>
/// <param name="Description">行项描述（中文）。</param>
/// <param name="Keywords">同义词 / 补充命中关键字。</param>
/// <param name="CategoryKey">目标分类（左导航项 key），取六大分类之一（见 <see cref="SettingsSearchIndex.CategoryKeys"/>）。</param>
/// <param name="AnchorId">滚动定位锚点 AutomationId（与 XAML 行项一致，非空）。</param>
public sealed record SettingsSearchEntry(
    string Title,
    string Description,
    string[] Keywords,
    string CategoryKey,
    string AnchorId);

/// <summary>
/// 设置搜索查询结果（设计文档 §9.4 / Req 2.8）。
/// 在命中条目之外额外暴露「结果超限」标志：当原始命中数超过结果上限（见
/// <see cref="SettingsSearchIndex.MaxResults"/>）时，<see cref="Entries"/> 已被截断且
/// <see cref="IsTruncated"/> 为 <c>true</c>，供 UI 给出「结果较多，请输入更精确的关键词」提示。
/// </summary>
/// <param name="Entries">截断后的命中条目（已按权重降序、同分按分类稳定排序），最多 <see cref="SettingsSearchIndex.MaxResults"/> 条。</param>
/// <param name="IsTruncated">原始命中数是否超过结果上限（被截断时为 <c>true</c>）。</param>
public sealed record SettingsSearchResult(
    IReadOnlyList<SettingsSearchEntry> Entries,
    bool IsTruncated);

/// <summary>
/// 设置搜索索引抽象（设计文档 §8.4.1 / §9.4）。
/// <see cref="Entries"/> 暴露全部静态可搜索条目；<see cref="Query"/> 执行不区分大小写的
/// 过滤 / 排序并施加结果上限。<see cref="Search"/> 在过滤结果之外额外暴露「结果超限」标志
/// （命中数超过结果上限时置位），供 UI 提示（过滤排序算法见任务 6.2 / §9.4）。
/// </summary>
public interface ISettingsSearchIndex
{
    /// <summary>全部静态可搜索条目（只读）。</summary>
    IReadOnlyList<SettingsSearchEntry> Entries { get; }

    /// <summary>按查询文本过滤并排序，返回命中条目（空 / 空白查询返回空列表）。</summary>
    IReadOnlyList<SettingsSearchEntry> Query(string text);

    /// <summary>
    /// 按查询文本过滤并排序，返回带「结果超限」标志的查询结果（空 / 空白查询返回空结果且标志为 <c>false</c>）。
    /// </summary>
    SettingsSearchResult Search(string text);
}

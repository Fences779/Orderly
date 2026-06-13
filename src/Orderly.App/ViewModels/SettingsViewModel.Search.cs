using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Orderly.App.ViewModels;

/// <summary>
/// 「设置页」搜索状态与命中跳转信号（任务 13.5，设计 §8.4.1 / §9.4 / Req 2.3、2.7、2.8）。
///
/// <para><b>职责</b>：承载设置搜索查询输入、命中结果集合、左导航选中分类与待滚动锚点；
/// 提供「激活搜索结果」命令以驱动 View 切换分类 + 滚动定位 + 高亮（命中导航信号），
/// 并暴露「结果超限」标志供 UI 给出「结果较多，请输入更精确的关键词」提示（Req 2.8）。</para>
///
/// <para><b>安全降级</b>：当 <see cref="ISettingsSearchIndex"/> 未注入（<c>_searchIndex == null</c>，
/// 完整 DI 注入见任务 21.1）时，搜索安全降级为空结果，不抛异常。</para>
///
/// <para><b>边界</b>：本分部仅负责 ViewModel 侧的搜索状态与命中信号（设计 §8.4.1）；XAML 搜索框接入、
/// 锚点 <c>BringIntoView</c> + 临时高亮附加行为由任务 17.2 实现。</para>
/// </summary>
public partial class SettingsViewModel
{
    /// <summary>左导航默认选中分类（§7.1 / Req 1.4，与 <see cref="SettingsSearchIndex.CategoryKeys"/> 首项一致）。</summary>
    private const string DefaultCategoryKey = "外观与启动";

    /// <summary>
    /// 设置搜索查询输入（绑定搜索框 <c>Box_SettingsSearch</c>，§9.4）。
    /// 变更时经 <see cref="OnSettingsSearchQueryChanged"/> 触发过滤并刷新 <see cref="SearchResults"/>。
    /// </summary>
    [ObservableProperty]
    private string settingsSearchQuery = string.Empty;

    /// <summary>
    /// 当前左导航选中分类 key（六大分类之一，默认「外观与启动」）。激活搜索结果时被设为命中条目所属分类
    /// （Req 2.3）；驱动右侧内容区显示对应分类内容（任务 17.x）。
    /// </summary>
    [ObservableProperty]
    private string selectedCategoryKey = DefaultCategoryKey;

    /// <summary>
    /// 待滚动定位的锚点 AutomationId（命中导航信号，§9.4 / Req 2.3）。激活搜索结果时被设为命中条目锚点，
    /// View 侧附加行为据此 <c>BringIntoView</c> 并施加短暂高亮（任务 17.2）；空结果不触发跳转时保持为 <c>null</c>（Req 2.7）。
    /// </summary>
    [ObservableProperty]
    private string? pendingScrollAnchorId;

    /// <summary>
    /// 「结果超限」信号（Req 2.8）：当某次查询的原始命中数超过结果上限
    /// （<see cref="SettingsSearchIndex.MaxResults"/> = 12）时置位，供 UI 显示
    /// 「结果较多，请输入更精确的关键词」提示；空 / 空白查询或未超限时为 <c>false</c>。
    /// </summary>
    [ObservableProperty]
    private bool isSearchResultsTruncated;

    /// <summary>
    /// 当前查询命中的搜索结果（已按命中权重降序、同分按分类稳定排序、最多 12 条）。
    /// 空 / 空白查询或搜索索引未注入时为空集合（§9.4 / Req 2.2）。
    /// </summary>
    public ObservableCollection<SettingsSearchEntry> SearchResults { get; } = new();

    /// <summary>
    /// 查询输入变更：经 <see cref="ISettingsSearchIndex.Search"/> 过滤 → 刷新 <see cref="SearchResults"/>
    /// 与 <see cref="IsSearchResultsTruncated"/>（§9.4 / Req 2.2、2.8）。
    /// 空 / 空白查询返回空结果（由索引保证）；索引未注入时安全降级为空结果（任务 21.1 完成完整注入）。
    /// </summary>
    partial void OnSettingsSearchQueryChanged(string value)
    {
        SearchResults.Clear();

        // 安全降级：搜索索引未注入时返回空结果（Req 2.2 空查询亦为空），不抛异常。
        if (_searchIndex is null)
        {
            IsSearchResultsTruncated = false;
            return;
        }

        var result = _searchIndex.Search(value);
        foreach (var entry in result.Entries)
        {
            SearchResults.Add(entry);
        }

        IsSearchResultsTruncated = result.IsTruncated;
    }

    /// <summary>
    /// 激活搜索结果命令（命中跳转信号，Req 2.3 / 2.7）：设 <see cref="SelectedCategoryKey"/> = 命中条目所属分类、
    /// <see cref="PendingScrollAnchorId"/> = 命中条目锚点，供 View 切换分类并滚动定位高亮。
    /// 当条目为空或当前无命中结果时不触发任何跳转（Req 2.7）。
    /// </summary>
    /// <param name="entry">被激活的命中条目（来自 <see cref="SearchResults"/>）。</param>
    [RelayCommand]
    private void ActivateSearchResult(SettingsSearchEntry? entry)
    {
        // 空结果 / 空条目不触发分类切换、滚动定位或高亮（Req 2.7）。
        if (entry is null || SearchResults.Count == 0)
        {
            return;
        }

        SelectedCategoryKey = entry.CategoryKey;
        PendingScrollAnchorId = entry.AnchorId;
    }
}

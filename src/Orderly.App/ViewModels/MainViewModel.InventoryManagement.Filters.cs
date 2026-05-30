using System;
using System.Collections.Generic;
using System.Linq;
using Orderly.Core.Models;

namespace Orderly.App.ViewModels;

public partial class MainViewModel
{
    private bool _isApplyingInventoryFilters;

    // 将接口过滤配置项渲染到下拉框
    private void UpdateInventoryFilterDropdowns()
    {
        if (InventoryDashboardResult?.FilterOptions == null) return;

        // 分类
        var categories = InventoryDashboardResult.FilterOptions.Categories ?? new List<string>();
        var prevCategory = SelectedInventoryCategory;
        
        InventoryCategoryOptions.Clear();
        InventoryCategoryOptions.Add("全部分类");
        foreach (var cat in categories)
        {
            if (!string.IsNullOrWhiteSpace(cat))
                InventoryCategoryOptions.Add(cat);
        }
        
        // 恢复之前选择的，如果还在列表中的话
        if (InventoryCategoryOptions.Contains(prevCategory))
        {
            SelectedInventoryCategory = prevCategory;
        }
        else
        {
            SelectedInventoryCategory = "全部分类";
        }

        // 状态
        var statuses = InventoryDashboardResult.FilterOptions.Statuses ?? new List<StringNarrationInventoryManagementDashboardFilterOption>();
        var prevStatus = SelectedInventoryStatus;

        InventoryStatusOptions.Clear();
        InventoryStatusOptions.Add("全部状态");
        foreach (var st in statuses)
        {
            if (st != null && !string.IsNullOrWhiteSpace(st.Label))
                InventoryStatusOptions.Add(st.Label);
        }

        // 恢复之前选择的，如果还在列表中的话
        if (InventoryStatusOptions.Contains(prevStatus))
        {
            SelectedInventoryStatus = prevStatus;
        }
        else
        {
            SelectedInventoryStatus = "全部状态";
        }
    }

    // 核心过滤与本地分页算法
    public void ApplyInventoryLocalFiltersAndPaging(bool resetToFirstPage = true)
    {
        if (_isApplyingInventoryFilters) return;
        _isApplyingInventoryFilters = true;

        try
        {
            if (InventoryDashboardResult?.Items == null)
            {
                PagedInventoryItems.Clear();
                InventoryTotalCount = 0;
                InventoryTotalPages = 1;
                InventoryCurrentPage = 1;
                InventoryPageNumbers.Clear();
                OnPropertyChanged(nameof(CanGoToPreviousInventoryPage));
                OnPropertyChanged(nameof(CanGoToNextInventoryPage));
                return;
            }

            if (resetToFirstPage)
            {
                InventoryCurrentPage = 1;
            }

            var query = InventoryDashboardResult.Items.AsEnumerable();

            // 1. 模糊搜索材料名称
            if (!string.IsNullOrWhiteSpace(InventoryKeyword))
            {
                var kw = InventoryKeyword.Trim();
                query = query.Where(item => item.MaterialName != null && item.MaterialName.Contains(kw, StringComparison.OrdinalIgnoreCase));
            }

            // 2. 分类过滤
            if (SelectedInventoryCategory != "全部分类" && !string.IsNullOrEmpty(SelectedInventoryCategory))
            {
                query = query.Where(item => string.Equals(item.Category, SelectedInventoryCategory, StringComparison.OrdinalIgnoreCase));
            }

            // 3. 状态过滤
            if (SelectedInventoryStatus != "全部状态" && !string.IsNullOrEmpty(SelectedInventoryStatus))
            {
                query = query.Where(item => string.Equals(item.StatusLabel, SelectedInventoryStatus, StringComparison.OrdinalIgnoreCase));
            }

            // 4. 排序
            var isDesc = SelectedInventorySortDirection == "desc";
            query = SelectedInventorySortBy switch
            {
                "30日动销" => isDesc ? query.OrderByDescending(i => i.Sold30dRatio) : query.OrderBy(i => i.Sold30dRatio),
                "7日动销" => isDesc ? query.OrderByDescending(i => i.Sold7dRatio) : query.OrderBy(i => i.Sold7dRatio),
                "当前库存" => isDesc ? query.OrderByDescending(i => i.CurrentStockQty) : query.OrderBy(i => i.CurrentStockQty),
                "安全库存建议" => isDesc ? query.OrderByDescending(i => i.SafeStockSuggestedQty ?? 0) : query.OrderBy(i => i.SafeStockSuggestedQty ?? 0),
                _ => query
            };

            var filteredItems = query.ToList();
            
            // 5. 计算分页
            InventoryTotalCount = filteredItems.Count;
            InventoryTotalPages = (int)Math.Ceiling((double)InventoryTotalCount / InventoryPageSize);
            if (InventoryTotalPages < 1) InventoryTotalPages = 1;

            if (InventoryCurrentPage > InventoryTotalPages)
            {
                InventoryCurrentPage = InventoryTotalPages;
            }

            // 6. 分页截取
            var itemsToDisplay = filteredItems
                .Skip((InventoryCurrentPage - 1) * InventoryPageSize)
                .Take(InventoryPageSize)
                .ToList();

            // 7. 更新展示集合
            PagedInventoryItems.Clear();
            foreach (var item in itemsToDisplay)
            {
                PagedInventoryItems.Add(item);
            }

            // 8. 重新生成分页导航按钮
            UpdatePageNumbers();

            // 9. 通知统计字句更新
            OnPropertyChanged(nameof(InventoryDashboardPageInfoText));
            OnPropertyChanged(nameof(HasInventoryDashboardItems));
            OnPropertyChanged(nameof(CanGoToPreviousInventoryPage));
            OnPropertyChanged(nameof(CanGoToNextInventoryPage));
        }
        finally
        {
            _isApplyingInventoryFilters = false;
        }
    }
}

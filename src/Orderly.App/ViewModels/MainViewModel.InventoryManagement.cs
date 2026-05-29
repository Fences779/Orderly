using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Orderly.Core.Models;

namespace Orderly.App.ViewModels;

public class InventoryPageItem
{
    public int PageNumber { get; set; }
    public bool IsCurrent { get; set; }
    public bool IsEllipsis => PageNumber == 0;
    public string DisplayText => PageNumber == 0 ? "..." : PageNumber.ToString();
}

public partial class MainViewModel
{
    // 库存分类筛选 (双向绑定分类 ComboBox)
    [ObservableProperty]
    private string _selectedInventoryCategory = "全部分类";

    // 库存状态筛选 (双向绑定状态 ComboBox)
    [ObservableProperty]
    private string _selectedInventoryStatus = "全部状态";

    // 库存视图切换 (精简, 动销, 库存)
    [ObservableProperty]
    private string _currentInventoryView = "精简";

    // 排序选项 (30日动销, 7日动销, 当前库存, 安全库存建议)
    [ObservableProperty]
    private string _selectedInventorySortBy = "30日动销";

    // 排序方向 (asc, desc)
    [ObservableProperty]
    private string _selectedInventorySortDirection = "desc";

    // 显示列选项 (用于 Popup 复选框控制列的显示隐藏)
    [ObservableProperty] private bool _showColMaterialName = true;
    [ObservableProperty] private bool _showColCategory = false;
    [ObservableProperty] private bool _showColCurrentStock = true;
    [ObservableProperty] private bool _showCol7dSold = true;
    [ObservableProperty] private bool _showCol30dSold = true;
    [ObservableProperty] private bool _showColSafeStock = true;
    [ObservableProperty] private bool _showColStatus = true;
    [ObservableProperty] private bool _showColUnitCost = false;
    [ObservableProperty] private bool _showColLastRestock = false;
    [ObservableProperty] private bool _showColSupplier = false;
    [ObservableProperty] private bool _showColRemark = false;

    // 分页状态
    [ObservableProperty]
    private int _inventoryCurrentPage = 1;

    [ObservableProperty]
    private int _inventoryPageSize = 10;

    [ObservableProperty]
    private int _inventoryTotalCount = 0;

    [ObservableProperty]
    private int _inventoryTotalPages = 1;

    // 是否可以切换上一页/下一页
    public bool CanGoToPreviousInventoryPage => InventoryCurrentPage > 1;
    public bool CanGoToNextInventoryPage => InventoryCurrentPage < InventoryTotalPages;

    // 页码集合 (用于分页按钮展示)
    public ObservableCollection<InventoryPageItem> InventoryPageNumbers { get; } = new();

    // 分页过滤后的库存展示集合 (绑定 to DataGrid)
    public ObservableCollection<StringNarrationInventoryManagementDashboardItem> PagedInventoryItems { get; } = new();

    // 下拉框数据源
    public ObservableCollection<string> InventoryCategoryOptions { get; } = new() { "全部分类" };
    public ObservableCollection<string> InventoryStatusOptions { get; } = new() { "全部状态" };
    public ObservableCollection<int> InventoryPageSizeOptions { get; } = new() { 10, 20, 50, 100 };
    public ObservableCollection<string> InventorySortOptions { get; } = new() 
    { 
        "30日动销", 
        "7日动销", 
        "当前库存", 
        "安全库存建议" 
    };

    // 属性变更关联回调，用于自动重新过滤/分页
    partial void OnSelectedInventoryCategoryChanged(string value) => ApplyInventoryLocalFiltersAndPaging();
    partial void OnSelectedInventoryStatusChanged(string value) => ApplyInventoryLocalFiltersAndPaging();
    partial void OnSelectedInventorySortByChanged(string value) => ApplyInventoryLocalFiltersAndPaging();
    partial void OnSelectedInventorySortDirectionChanged(string value) => ApplyInventoryLocalFiltersAndPaging();
    
    partial void OnInventoryCurrentPageChanged(int value) => ApplyInventoryLocalFiltersAndPaging(false);
    partial void OnInventoryPageSizeChanged(int value)
    {
        InventoryCurrentPage = 1;
        ApplyInventoryLocalFiltersAndPaging();
    }

    partial void OnInventoryKeywordChanged(string value) => ApplyInventoryLocalFiltersAndPaging();

    // 监听 API 结果变化
    partial void OnInventoryDashboardResultChanged(StringNarrationInventoryManagementDashboardResult value)
    {
        UpdateInventoryFilterDropdowns();
        ApplyInventoryLocalFiltersAndPaging();
    }

    // 视图切换命令 (修改列隐藏/显示)
    [RelayCommand]
    private void SwitchInventoryView(string viewName)
    {
        CurrentInventoryView = viewName;
        ApplyViewColumnVisibility(viewName);
    }

    // 页码切换命令
    [RelayCommand]
    private void ChangeInventoryPage(int targetPage)
    {
        if (targetPage < 1 || targetPage > InventoryTotalPages) return;
        InventoryCurrentPage = targetPage;
    }

    // 上一页/下一页简易命令，省去 XAML 数学计算 Converter
    [RelayCommand]
    private void GoToPreviousInventoryPage()
    {
        if (CanGoToPreviousInventoryPage)
        {
            InventoryCurrentPage--;
        }
    }

    [RelayCommand]
    private void GoToNextInventoryPage()
    {
        if (CanGoToNextInventoryPage)
        {
            InventoryCurrentPage++;
        }
    }

    // 切换排序方向
    [RelayCommand]
    private void ToggleInventorySortDirection()
    {
        SelectedInventorySortDirection = SelectedInventorySortDirection == "desc" ? "asc" : "desc";
    }

    // 根据选择的视图自动重置显示列
    private void ApplyViewColumnVisibility(string viewName)
    {
        switch (viewName)
        {
            case "精简":
                ShowColMaterialName = true;
                ShowColCategory = false;
                ShowColCurrentStock = true;
                ShowCol7dSold = true;
                ShowCol30dSold = true;
                ShowColSafeStock = true;
                ShowColStatus = true;
                ShowColUnitCost = false;
                ShowColLastRestock = false;
                ShowColSupplier = false;
                ShowColRemark = false;
                break;
            case "动销":
                ShowColMaterialName = true;
                ShowColCategory = true;
                ShowColCurrentStock = true;
                ShowCol7dSold = true;
                ShowCol30dSold = true;
                ShowColSafeStock = false;
                ShowColStatus = true;
                ShowColUnitCost = false;
                ShowColLastRestock = false;
                ShowColSupplier = false;
                ShowColRemark = false;
                break;
            case "库存":
                ShowColMaterialName = true;
                ShowColCategory = true;
                ShowColCurrentStock = true;
                ShowCol7dSold = false;
                ShowCol30dSold = false;
                ShowColSafeStock = true;
                ShowColStatus = true;
                ShowColUnitCost = true;
                ShowColLastRestock = true;
                ShowColSupplier = true;
                ShowColRemark = true;
                break;
        }
    }

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

    // 生成数字页码包装列表
    private void UpdatePageNumbers()
    {
        InventoryPageNumbers.Clear();
        int total = InventoryTotalPages;
        int current = InventoryCurrentPage;

        void AddPage(int p)
        {
            InventoryPageNumbers.Add(new InventoryPageItem 
            { 
                PageNumber = p, 
                IsCurrent = (p == current) 
            });
        }

        if (total <= 7)
        {
            for (int i = 1; i <= total; i++)
                AddPage(i);
        }
        else
        {
            if (current <= 4)
            {
                for (int i = 1; i <= 5; i++)
                    AddPage(i);
                AddPage(0); // 省略号
                AddPage(total);
            }
            else if (current >= total - 3)
            {
                AddPage(1);
                AddPage(0); // 省略号
                for (int i = total - 4; i <= total; i++)
                    AddPage(i);
            }
            else
            {
                AddPage(1);
                AddPage(0); // 省略号
                AddPage(current - 1);
                AddPage(current);
                AddPage(current + 1);
                AddPage(0); // 省略号
                AddPage(total);
            }
        }
    }
}

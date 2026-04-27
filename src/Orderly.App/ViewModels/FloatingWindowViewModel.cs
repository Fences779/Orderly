using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Orderly.Core.Models;
using Orderly.Core.Repositories;
using Orderly.Core.Services;

namespace Orderly.App.ViewModels;

public partial class FloatingWindowViewModel : ObservableObject
{
    private readonly IOrderRepository _orderRepository;
    private readonly IReplyTemplateRepository _replyTemplateRepository;
    private readonly IClipboardService _clipboardService;
    private readonly List<OrderListItem> _allOrders = new();

    public FloatingWindowViewModel(
        IOrderRepository orderRepository,
        IReplyTemplateRepository replyTemplateRepository,
        IClipboardService clipboardService)
    {
        _orderRepository = orderRepository;
        _replyTemplateRepository = replyTemplateRepository;
        _clipboardService = clipboardService;
    }

    public ObservableCollection<OrderListItem> Orders { get; } = new();
    public ObservableCollection<ReplyTemplate> FavoriteTemplates { get; } = new();

    [ObservableProperty]
    private string searchKeyword = string.Empty;

    [ObservableProperty]
    private string statusMessage = "悬浮助手已就绪";

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var orders = await _orderRepository.GetRecentAsync(cancellationToken);
        var templates = await _replyTemplateRepository.GetFavoritesAsync(cancellationToken);

        _allOrders.Clear();
        _allOrders.AddRange(orders.Select(order => new OrderListItem(order)));

        FavoriteTemplates.Clear();
        foreach (var template in templates.Take(6))
        {
            FavoriteTemplates.Add(template);
        }

        ApplyFilter();
    }

    partial void OnSearchKeywordChanged(string value)
    {
        ApplyFilter();
    }

    [RelayCommand]
    private void CopyTemplate(ReplyTemplate? template)
    {
        if (template is null)
        {
            return;
        }

        _clipboardService.SetText(template.Content);
        StatusMessage = $"已复制：{template.Title}";
    }

    private void ApplyFilter()
    {
        var keyword = SearchKeyword.Trim();
        var rows = string.IsNullOrWhiteSpace(keyword)
            ? _allOrders
            : _allOrders
                .Where(item =>
                    item.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                    item.CustomerName.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                    item.PlatformText.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                .ToList();

        Orders.Clear();
        foreach (var item in rows.Take(6))
        {
            Orders.Add(item);
        }
    }
}

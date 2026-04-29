using Orderly.Core.Models;

namespace Orderly.App.ViewModels;

public partial class MainViewModel
{
    private async Task ApplyNavigationRouteAsync(NavigationRouteResult route, string title, string selectedSection)
    {
        CurrentNavigationTarget = route.RequestedTarget ?? route.ResolvedTarget;
        LastNavigationStatus = route.StatusMessage;
        LastNavigationError = route.CanNavigate ? string.Empty : route.DisabledReason;

        if (!route.CanNavigate || route.ResolvedTarget is null)
        {
            StatusMessage = string.IsNullOrWhiteSpace(route.DisabledReason)
                ? $"无法定位：{title}"
                : $"无法定位：{route.DisabledReason}";
            return;
        }

        var target = route.ResolvedTarget;

        _isSynchronizingSelection = true;
        try
        {
            SelectedSection = selectedSection;

            if (target.OrderId is int orderId)
            {
                SelectOrderById(orderId);
            }

            if (target.CustomerId is int customerId)
            {
                SelectCustomerById(customerId);
            }
        }
        finally
        {
            _isSynchronizingSelection = false;
        }

        if (target.CustomerId is int selectedCustomerId)
        {
            var customer = SelectedCustomer?.Id == selectedCustomerId
                ? SelectedCustomer
                : Customers.FirstOrDefault(candidate => candidate.Id == selectedCustomerId)
                    ?? _allCustomers.FirstOrDefault(candidate => candidate.Id == selectedCustomerId);
            if (customer is not null)
            {
                SelectedCustomer = customer;
                await ReloadSelectedCustomerDetailsAsync(customer);
                await SyncNavigationTargetAsync(target, customer.Id);
            }
        }

        StatusMessage = route.UsedFallback
            ? $"已回退定位：{title}"
            : route.RequiresUserAction
                ? $"已定位：{title}（需手动执行后续动作）"
                : $"已定位：{title}";
    }

    private async Task SyncNavigationTargetAsync(NavigationTarget target, int customerId)
    {
        if (target.TargetSection == NavigationTargetSection.AiSuggestion && target.RelatedEntityId is int aiSuggestionId)
        {
            SelectedAiSuggestion = AiSuggestions.FirstOrDefault(item => item.Id == aiSuggestionId) ?? SelectedAiSuggestion;
            return;
        }

        if (target.TargetSection == NavigationTargetSection.Ocr && target.RelatedEntityId is int ocrResultId && CurrentOcrResult?.Id != ocrResultId)
        {
            var ocrResults = await _ocrService.ListByCustomerAsync(customerId);
            var matched = ocrResults.FirstOrDefault(item => item.Id == ocrResultId);
            if (matched is not null)
            {
                CurrentOcrResult = matched;
            }
        }
    }
}

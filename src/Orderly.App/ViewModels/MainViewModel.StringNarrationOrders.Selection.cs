using Orderly.Core.Models;

namespace Orderly.App.ViewModels;

// UI selection-state coordination for the string-narration order list/detail.
// These methods manage which summary/detail is currently selected and how the
// list re-syncs after a refresh. They do NOT perform payment verification,
// paid-handling, WeChat shipping sync, or the payment-success-to-fulfillment
// transaction loop — those remain in the core partial untouched.
public partial class MainViewModel
{
    private void SelectStringNarrationSummaryByDetail(StringNarrationOrderDetail? detail)
    {
        if (detail is null)
        {
            return;
        }

        var match = StringNarrationOrders.FirstOrDefault(item =>
            string.Equals(item.OrderNo, detail.OrderNo, StringComparison.Ordinal)
            || string.Equals(item.Id, detail.Id, StringComparison.Ordinal)
            || string.Equals(item.WxOutTradeNo, detail.WxOutTradeNo, StringComparison.Ordinal));
        if (match is not null && SelectedStringNarrationOrder != match)
        {
            _isSynchronizingStringNarrationSelection = true;
            try
            {
                SelectedStringNarrationOrder = match;
            }
            finally
            {
                _isSynchronizingStringNarrationSelection = false;
            }
        }
    }

    public void DismissStringNarrationDetailsForSession()
    {
        _hasDismissedStringNarrationDetailsThisSession = true;
        ClearStringNarrationSelection();
        StringNarrationStatusMessage = "已关闭订单详情，本次会话内不会自动展开。";
    }

    public void EnsureStringNarrationDetailSelection()
    {
        if (_hasDismissedStringNarrationDetailsThisSession
            || StringNarrationOrders.Count == 0
            || SelectedStringNarrationOrder is not null
            || SelectedStringNarrationOrderDetail is not null)
        {
            return;
        }

        _ = OpenStringNarrationOrderDetailAsync(StringNarrationOrders.FirstOrDefault());
    }

    public async Task OpenStringNarrationOrderDetailAsync(StringNarrationOrderSummary? summary)
    {
        if (summary is null)
        {
            return;
        }

        _hasDismissedStringNarrationDetailsThisSession = false;
        if (SelectedStringNarrationOrder != summary)
        {
            SelectedStringNarrationOrder = summary;
        }

        await LoadStringNarrationOrderDetailAsync(summary);
    }

    private void UpdateStringNarrationSummary(StringNarrationOrderDetail detail)
    {
        var index = StringNarrationOrders.ToList().FindIndex(item =>
            string.Equals(item.OrderNo, detail.OrderNo, StringComparison.Ordinal)
            || string.Equals(item.Id, detail.Id, StringComparison.Ordinal));
        if (index < 0)
        {
            return;
        }

        _isSynchronizingStringNarrationSelection = true;
        try
        {
            var previousSummary = StringNarrationOrders[index];
            StringNarrationOrders[index] = detail;
            SelectedStringNarrationOrder = StringNarrationOrders[index];
            UpsertExceptionOrder(detail);
            SynchronizeStringNarrationStatsForSummaryChange(previousSummary, detail);
        }
        finally
        {
            _isSynchronizingStringNarrationSelection = false;
        }
    }

    private void ClearStringNarrationSelection()
    {
        _isSynchronizingStringNarrationSelection = true;
        try
        {
            SelectedStringNarrationOrder = null;
        }
        finally
        {
            _isSynchronizingStringNarrationSelection = false;
        }

        SelectedStringNarrationOrderDetail = null;
        StringNarrationLeftPaneMode = StringNarrationLeftPaneOrderList;
    }

    private void RestoreStringNarrationSelection(StringNarrationSelectionSnapshot previousSelection)
    {
        var matchedOrder = FindStringNarrationOrder(previousSelection);
        if (matchedOrder is not null)
        {
            SelectedStringNarrationOrder = matchedOrder;
            if (previousSelection.ShouldOpenDetail)
            {
                _ = OpenStringNarrationOrderDetailAsync(matchedOrder);
            }
            return;
        }

        // 找不到历史选择时，默认不再选中第一个，而是清除选择状态
        ClearStringNarrationSelection();
    }

    private StringNarrationOrderSummary? FindStringNarrationOrder(StringNarrationSelectionSnapshot snapshot)
    {
        if (snapshot.IsEmpty)
        {
            return null;
        }

        return StringNarrationOrders.FirstOrDefault(item =>
            (!string.IsNullOrWhiteSpace(snapshot.OrderNo) && string.Equals(item.OrderNo, snapshot.OrderNo, StringComparison.Ordinal))
            || (!string.IsNullOrWhiteSpace(snapshot.Id) && string.Equals(item.Id, snapshot.Id, StringComparison.Ordinal))
            || (!string.IsNullOrWhiteSpace(snapshot.TradeNo) && string.Equals(item.WxOutTradeNo, snapshot.TradeNo, StringComparison.Ordinal)));
    }

    private StringNarrationSelectionSnapshot CaptureStringNarrationSelection()
    {
        var detail = SelectedStringNarrationOrderDetail;
        if (detail is not null)
        {
            return new StringNarrationSelectionSnapshot(detail.OrderNo, detail.WxOutTradeNo, detail.Id, true);
        }

        var summary = SelectedStringNarrationOrder;
        return summary is null
            ? StringNarrationSelectionSnapshot.Empty
            : new StringNarrationSelectionSnapshot(summary.OrderNo, summary.WxOutTradeNo, summary.Id, false);
    }
}

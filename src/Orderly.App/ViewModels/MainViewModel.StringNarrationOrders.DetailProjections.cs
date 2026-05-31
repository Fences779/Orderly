namespace Orderly.App.ViewModels;

// Read-only detail / production-sheet display projection properties. Pure getters over
// SelectedStringNarrationOrderDetail / SelectedStringNarrationProductionSheet; no side
// effects, no gateway, no transaction logic.
public partial class MainViewModel
{
    public string StringNarrationEmptyStateText => string.IsNullOrWhiteSpace(StringNarrationError)
        ? "暂无串述订单，点击刷新从 adminPcGateway 拉取。"
        : StringNarrationError;
    public string StringNarrationSelectedTitle => string.IsNullOrWhiteSpace(SelectedStringNarrationOrderDetail?.TitleSnapshot)
        ? "未选择串述订单"
        : SelectedStringNarrationOrderDetail.TitleSnapshot;
    public string StringNarrationSelectedOrderNo => string.IsNullOrWhiteSpace(SelectedStringNarrationOrderDetail?.OrderNo)
        ? "无 orderNo"
        : SelectedStringNarrationOrderDetail.OrderNo;
    public string StringNarrationSelectedTradeNo => string.IsNullOrWhiteSpace(SelectedStringNarrationOrderDetail?.WxOutTradeNo)
        ? "无 tradeNo"
        : SelectedStringNarrationOrderDetail.WxOutTradeNo;
    public string StringNarrationSelectedAmountText => SelectedStringNarrationOrderDetail is null
        ? "¥0"
        : $"¥{SelectedStringNarrationOrderDetail.Amount:N0}";
    public string StringNarrationSelectedPaidAtText => FormatGatewayTime(SelectedStringNarrationOrderDetail?.PaidAt ?? 0);
    public string StringNarrationSelectedCreatedAtText => FormatGatewayTime(SelectedStringNarrationOrderDetail?.CreatedAt ?? 0);
    public string StringNarrationAddressText => SelectedStringNarrationOrderDetail?.Address is null
        ? "暂无收件信息"
        : BuildAddressText(SelectedStringNarrationOrderDetail.Address);
    public string StringNarrationRemarkText => string.IsNullOrWhiteSpace(SelectedStringNarrationOrderDetail?.Remark)
        ? "暂无买家备注"
        : SelectedStringNarrationOrderDetail.Remark;
    public string StringNarrationShippingStateText => SelectedStringNarrationOrderDetail is null
        ? "暂无履约信息"
        : $"支付/订单状态：{SelectedStringNarrationOrderDetail.StatusText} / 履约状态：{SelectedStringNarrationOrderDetail.FulfillmentStatusLabel} ({SelectedStringNarrationOrderDetail.FulfillmentStatus}) / 微信发货同步：{SelectedStringNarrationOrderDetail.WxShippingSyncStatusText}";
    public string StringNarrationTrackingText => SelectedStringNarrationOrderDetail is null
        ? "暂无物流"
        : $"{BuildValue(SelectedStringNarrationOrderDetail.Carrier, "未填快递公司")} {BuildValue(SelectedStringNarrationOrderDetail.ExpressCompanyCode, "未填编码")} {BuildValue(SelectedStringNarrationOrderDetail.TrackingNo, "未填单号")}";
    public string StringNarrationFulfillmentTimeText => SelectedStringNarrationOrderDetail is null
        ? "暂无履约时间"
        : $"shippedAt：{SelectedStringNarrationOrderDetail.ShippedAtText} / completedAt：{SelectedStringNarrationOrderDetail.CompletedAtText} / fulfillmentUpdatedAt：{SelectedStringNarrationOrderDetail.FulfillmentUpdatedAtText}";
    public string StringNarrationDetailOrderNo => SelectedStringNarrationOrderDetail?.OrderNoText ?? "无 orderNo";
    public string StringNarrationDetailTransactionId => SelectedStringNarrationOrderDetail?.WxTransactionIdText ?? "无 wxTransactionId";
    public string StringNarrationDetailStatus => SelectedStringNarrationOrderDetail is null
        ? "未选择订单"
        : $"{SelectedStringNarrationOrderDetail.StatusText} / {SelectedStringNarrationOrderDetail.FulfillmentStatusLabel}";
    public string StringNarrationDetailProduct => SelectedStringNarrationOrderDetail is null
        ? "暂无商品信息"
        : $"{SelectedStringNarrationOrderDetail.TitleSnapshotText} / {SelectedStringNarrationOrderDetail.ItemsSnapshotStateText}";
    public string StringNarrationDetailReceiver => SelectedStringNarrationOrderDetail is null
        ? "暂无收货信息"
        : $"{SelectedStringNarrationOrderDetail.ReceiverSummaryText}{Environment.NewLine}{SelectedStringNarrationOrderDetail.FullAddressText}";
    public string StringNarrationDetailProduction => SelectedStringNarrationOrderDetail?.ProductionOrderSummaryText ?? "暂无制作单";
    public string StringNarrationProductionSheetOrderNoText => SelectedStringNarrationOrderDetail?.OrderNoText ?? "无 orderNo";
    public string StringNarrationProductionSheetProductionOrderNoText => SelectedStringNarrationProductionSheet?.ProductionOrderNoText ?? "无制作单号";
    public string StringNarrationProductionSheetWorkOrderNoText => SelectedStringNarrationProductionSheet?.WorkOrderNoText ?? "无工单号";
    public string StringNarrationProductionSheetStatusText => SelectedStringNarrationProductionSheet?.WorkOrderStatusText ?? "未知工单状态";
    public string StringNarrationProductionSheetArrangementText => SelectedStringNarrationProductionSheet?.ArrangementDisplayText ?? "未提供排列方式";
    public string StringNarrationProductionSheetRemarkText => SelectedStringNarrationProductionSheet?.RemarkText ?? "无制作备注";
    public string StringNarrationProductionSheetExampleImageUrl => SelectedStringNarrationProductionSheet?.ExampleImageUrl ?? string.Empty;
    public string StringNarrationProductionSheetExampleFallbackText => SelectedStringNarrationProductionSheet?.ExampleImageFallbackText ?? "未提供例图";
}

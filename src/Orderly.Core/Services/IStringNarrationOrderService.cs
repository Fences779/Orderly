using Orderly.Core.Models;

namespace Orderly.Core.Services;

public interface IStringNarrationOrderService
{
    Task<StringNarrationWhoamiResult> WhoamiAsync(CancellationToken cancellationToken = default);

    Task<StringNarrationOrderListResult> GetOrdersAsync(StringNarrationOrderQuery query, CancellationToken cancellationToken = default);

    Task<StringNarrationFulfillmentStats> GetFulfillmentStatsAsync(StringNarrationOrderQuery query, CancellationToken cancellationToken = default);

    Task<StringNarrationOrderDetail> GetOrderDetailAsync(string orderNo, string tradeNo = "", string id = "", CancellationToken cancellationToken = default);

    Task<StringNarrationOrderDetail> UpdateFulfillmentAsync(StringNarrationFulfillmentUpdateRequest request, CancellationToken cancellationToken = default);

    Task<StringNarrationOrderDetail> GenerateProductionOrderAsync(StringNarrationGenerateProductionOrderRequest request, CancellationToken cancellationToken = default);
}

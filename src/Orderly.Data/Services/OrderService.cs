using Orderly.Core.Models;
using Orderly.Core.Repositories;
using Orderly.Core.Services;

namespace Orderly.Data.Services;

public sealed class OrderService : IOrderService
{
    private const int MaxOrderTitleCharacters = 120;
    private const int MaxRequirementCharacters = 2000;
    private const int MaxShortFieldCharacters = 80;
    private const int MaxExternalIdCharacters = 160;
    private const int MaxRawPayloadCharacters = 4096;
    private const decimal MaxOrderAmount = 100_000_000m;

    private readonly IOrderRepository _orderRepository;
    private readonly IActivityLogRepository _activityLogRepository;

    public OrderService(IOrderRepository orderRepository, IActivityLogRepository activityLogRepository)
    {
        _orderRepository = orderRepository;
        _activityLogRepository = activityLogRepository;
    }

    public async Task<IReadOnlyList<Order>> GetOrdersAsync(CancellationToken cancellationToken = default)
    {
        return await _orderRepository.GetRecentAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Order>> GetCustomerOrdersAsync(int customerId, CancellationToken cancellationToken = default)
    {
        return await _orderRepository.ListByCustomerIdAsync(customerId, cancellationToken);
    }

    public async Task<Order?> GetOrderAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _orderRepository.GetByIdAsync(id, cancellationToken);
    }

    public async Task<Order> SaveOrderAsync(Order order, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(order);

        var merchantOrder = ToMerchantOrder(order);
        NormalizeOrder(merchantOrder);
        if (merchantOrder.Id <= 0)
        {
            var created = await _orderRepository.CreateAsync(merchantOrder, cancellationToken);
            await AddActivityAsync(ActivityType.OrderCreated, created.CustomerId, created.DealId, created.Id, "创建订单", created.Title, cancellationToken);
            return created;
        }

        var existing = await _orderRepository.GetByIdAsync(merchantOrder.Id, cancellationToken);
        await _orderRepository.UpdateAsync(merchantOrder, cancellationToken);

        if (existing is not null && existing.Status != merchantOrder.Status)
        {
            await AddActivityAsync(
                ActivityType.OrderStatusChanged,
                merchantOrder.CustomerId,
                merchantOrder.DealId,
                merchantOrder.Id,
                "订单状态变更",
                $"{OrderStatusCatalog.GetLabel(existing.Status)} -> {OrderStatusCatalog.GetLabel(merchantOrder.Status)}",
                cancellationToken);
        }

        return merchantOrder;
    }

    public async Task UpdateStatusAsync(int id, OrderStatus status, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(status))
        {
            throw new InvalidOperationException("订单状态无效。");
        }

        var order = await _orderRepository.GetByIdAsync(id, cancellationToken);
        if (order is null || order.Status == status)
        {
            return;
        }

        var oldStatus = order.Status;
        order.Status = status;
        await _orderRepository.UpdateAsync(order, cancellationToken);
        await AddActivityAsync(
            ActivityType.OrderStatusChanged,
            order.CustomerId,
            order.DealId,
            order.Id,
            "订单状态变更",
            $"{OrderStatusCatalog.GetLabel(oldStatus)} -> {OrderStatusCatalog.GetLabel(status)}",
            cancellationToken);
    }

    private static MerchantOrder ToMerchantOrder(Order order)
    {
        return new MerchantOrder
        {
            Id = order.Id,
            CustomerId = order.CustomerId,
            DealId = order.DealId,
            Title = order.Title,
            Status = order.Status,
            Amount = order.Amount,
            Requirement = order.Requirement,
            SourcePlatform = order.SourcePlatform,
            Channel = order.Channel,
            ExternalId = order.ExternalId,
            RawPayload = order.RawPayload,
            NextFollowUpAt = order.NextFollowUpAt,
            Customer = order.Customer,
            CreatedAt = order.CreatedAt,
            UpdatedAt = order.UpdatedAt,
            DeletedAt = order.DeletedAt,
            RemoteId = order.RemoteId,
            IsSynced = order.IsSynced,
            Version = order.Version
        };
    }

    private Task AddActivityAsync(ActivityType type, int? customerId, int? dealId, int? orderId, string title, string description, CancellationToken cancellationToken)
    {
        return _activityLogRepository.CreateAsync(new ActivityLog
        {
            Type = type,
            CustomerId = customerId,
            DealId = dealId,
            OrderId = orderId,
            Title = title,
            Description = description,
            Operator = "local"
        }, cancellationToken);
    }

    private static void NormalizeOrder(MerchantOrder order)
    {
        if (order.CustomerId <= 0)
        {
            throw new InvalidOperationException("订单缺少有效客户。");
        }

        if (!Enum.IsDefined(order.Status))
        {
            throw new InvalidOperationException("订单状态无效。");
        }

        if (order.Amount < 0 || order.Amount > MaxOrderAmount)
        {
            throw new InvalidOperationException("订单金额超出允许范围。");
        }

        order.Title = NormalizeRequiredText(order.Title, MaxOrderTitleCharacters, "订单标题");
        order.Requirement = NormalizeOptionalText(order.Requirement, MaxRequirementCharacters, "订单需求");
        order.SourcePlatform = NormalizeOptionalText(order.SourcePlatform, MaxShortFieldCharacters, "订单来源平台");
        order.Channel = NormalizeOptionalText(order.Channel, MaxShortFieldCharacters, "订单渠道");
        order.ExternalId = NormalizeOptionalText(order.ExternalId, MaxExternalIdCharacters, "订单外部标识");
        order.RawPayload = NormalizeOptionalText(order.RawPayload, MaxRawPayloadCharacters, "订单原始载荷");
    }

    private static string NormalizeRequiredText(string? value, int maxCharacters, string fieldName)
    {
        var normalized = NormalizeOptionalText(value, maxCharacters, fieldName);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException($"{fieldName}不能为空。");
        }

        return normalized;
    }

    private static string NormalizeOptionalText(string? value, int maxCharacters, string fieldName)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length > maxCharacters)
        {
            throw new InvalidOperationException($"{fieldName}不能超过 {maxCharacters} 个字符。");
        }

        if (normalized.Any(static ch => char.IsControl(ch) && ch is not '\r' and not '\n' and not '\t'))
        {
            throw new InvalidOperationException($"{fieldName}不能包含控制字符。");
        }

        return normalized;
    }
}

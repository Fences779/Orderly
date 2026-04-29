using Orderly.Core.Models;
using Orderly.Core.Repositories;
using Orderly.Core.Services;

namespace Orderly.Data.Services;

public sealed class LocalNavigationRouteService : INavigationRouteService
{
    private readonly ICustomerRepository _customerRepository;
    private readonly IOrderRepository _orderRepository;
    private readonly IConversationMessageRepository _messageRepository;
    private readonly IAiSuggestionRepository _suggestionRepository;
    private readonly IOcrResultRepository _ocrResultRepository;
    private readonly IFollowUpRepository _followUpRepository;
    private readonly IActivityLogRepository _activityLogRepository;

    public LocalNavigationRouteService(
        ICustomerRepository customerRepository,
        IOrderRepository orderRepository,
        IConversationMessageRepository messageRepository,
        IAiSuggestionRepository suggestionRepository,
        IOcrResultRepository ocrResultRepository,
        IFollowUpRepository followUpRepository,
        IActivityLogRepository activityLogRepository)
    {
        _customerRepository = customerRepository;
        _orderRepository = orderRepository;
        _messageRepository = messageRepository;
        _suggestionRepository = suggestionRepository;
        _ocrResultRepository = ocrResultRepository;
        _followUpRepository = followUpRepository;
        _activityLogRepository = activityLogRepository;
    }

    public Task<NavigationRouteResult> ResolveAsync(SearchResultItem item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        return ResolveCoreAsync(
            item.TargetSection,
            item.ActionHint,
            item.CustomerId,
            item.OrderId,
            item.RelatedEntityType,
            item.RelatedEntityId,
            requiresUserAction: false,
            isEnabled: true,
            disabledReason: string.Empty,
            cancellationToken);
    }

    public Task<NavigationRouteResult> ResolveAsync(WorkbenchTask task, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(task);
        return ResolveCoreAsync(
            task.TargetSection,
            task.ActionHint,
            task.CustomerId,
            task.OrderId,
            task.RelatedEntityType,
            task.RelatedEntityId,
            requiresUserAction: false,
            isEnabled: true,
            disabledReason: string.Empty,
            cancellationToken);
    }

    public Task<NavigationRouteResult> ResolveAsync(QuickAction action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        return ResolveCoreAsync(
            action.TargetSection,
            action.ActionHint,
            action.CustomerId,
            action.OrderId,
            action.RelatedEntityType,
            action.RelatedEntityId,
            action.RequiresUserAction,
            action.IsEnabled,
            action.DisabledReason,
            cancellationToken);
    }

    private async Task<NavigationRouteResult> ResolveCoreAsync(
        string targetSectionValue,
        string actionHintValue,
        int? customerId,
        int? orderId,
        string relatedEntityType,
        int? relatedEntityId,
        bool requiresUserAction,
        bool isEnabled,
        string disabledReason,
        CancellationToken cancellationToken)
    {
        if (!isEnabled)
        {
            return Fail(disabledReason, BuildRequestedTargetOrNull(
                targetSectionValue,
                actionHintValue,
                customerId,
                orderId,
                relatedEntityType,
                relatedEntityId,
                requiresUserAction));
        }

        if (!TryBuildRequestedTarget(
            targetSectionValue,
            actionHintValue,
            customerId,
            orderId,
            relatedEntityType,
            relatedEntityId,
            requiresUserAction,
            out var requestedTarget,
            out var buildFailureReason))
        {
            return Fail(buildFailureReason, requestedTarget);
        }

        var normalizedRequestedTarget = requestedTarget ?? throw new InvalidOperationException("Navigation target should not be null after successful route normalization.");

        var customer = normalizedRequestedTarget.CustomerId is int resolvedCustomerId
            ? await _customerRepository.GetByIdAsync(resolvedCustomerId, cancellationToken)
            : null;
        var order = normalizedRequestedTarget.OrderId is int resolvedOrderId
            ? await _orderRepository.GetByIdAsync(resolvedOrderId, cancellationToken)
            : null;

        if (customer is null && order?.CustomerId is > 0)
        {
            customer = await _customerRepository.GetByIdAsync(order.CustomerId, cancellationToken);
        }

        if (normalizedRequestedTarget.TargetSection is NavigationTargetSection.Customer or NavigationTargetSection.Order)
        {
            var directTarget = normalizedRequestedTarget.TargetSection == NavigationTargetSection.Order && order is not null
                ? CreateResolvedTarget(normalizedRequestedTarget, customer, order)
                : normalizedRequestedTarget.TargetSection == NavigationTargetSection.Customer && customer is not null
                    ? CreateResolvedTarget(normalizedRequestedTarget, customer, order)
                    : null;

            if (directTarget is not null)
            {
                return Success(directTarget, normalizedRequestedTarget, usedFallback: false);
            }

            var fallback = BuildFallbackTarget(customer, order);
            return fallback is not null
                ? Success(fallback, normalizedRequestedTarget, usedFallback: true, "深链实体不可用，已回退到客户/订单定位。")
                : Fail("目标实体不存在，无法定位客户或订单。", normalizedRequestedTarget);
        }

        var relatedExists = await RelatedEntityExistsAsync(normalizedRequestedTarget, cancellationToken);
        if (relatedExists)
        {
            if (customer is not null || order is not null)
            {
                return Success(CreateResolvedTarget(normalizedRequestedTarget, customer, order), normalizedRequestedTarget, usedFallback: false);
            }

            return Fail("深链实体存在，但缺少客户或订单定位上下文。", normalizedRequestedTarget);
        }

        var fallbackTarget = BuildFallbackTarget(customer, order);
        return fallbackTarget is not null
            ? Success(fallbackTarget, normalizedRequestedTarget, usedFallback: true, "深链实体缺失，已回退到客户/订单定位。")
            : Fail("深链实体缺失，且没有可用的客户或订单定位。", normalizedRequestedTarget);
    }

    private static bool TryBuildRequestedTarget(
        string targetSectionValue,
        string actionHintValue,
        int? customerId,
        int? orderId,
        string relatedEntityType,
        int? relatedEntityId,
        bool requiresUserAction,
        out NavigationTarget? requestedTarget,
        out string failureReason)
    {
        requestedTarget = null;
        failureReason = string.Empty;

        if (!NavigationSemantics.TryParseTargetSection(targetSectionValue, out var targetSection))
        {
            failureReason = $"未知 TargetSection：{targetSectionValue}";
            return false;
        }

        if (!NavigationSemantics.TryParseActionHint(actionHintValue, out var actionHint))
        {
            failureReason = $"未知 ActionHint：{actionHintValue}";
            return false;
        }

        if (targetSection == NavigationTargetSection.Unknown)
        {
            targetSection = NavigationSemantics.GetDefaultTargetSection(actionHint);
        }

        if (actionHint == NavigationActionHint.None)
        {
            actionHint = NavigationSemantics.GetDefaultActionHint(targetSection, customerId, orderId);
        }

        if (targetSection == NavigationTargetSection.Unknown && actionHint != NavigationActionHint.None)
        {
            targetSection = NavigationSemantics.GetDefaultTargetSection(actionHint);
        }

        if (targetSection == NavigationTargetSection.Unknown || actionHint == NavigationActionHint.None)
        {
            failureReason = "缺少可识别的路由语义。";
            return false;
        }

        requestedTarget = new NavigationTarget
        {
            TargetSection = targetSection,
            ActionHint = actionHint,
            CustomerId = customerId,
            OrderId = orderId,
            RelatedEntityType = relatedEntityType ?? string.Empty,
            RelatedEntityId = relatedEntityId,
            RequiresUserAction = requiresUserAction || NavigationSemantics.IsHighRiskAction(actionHint)
        };
        return true;
    }

    private static NavigationTarget? BuildRequestedTargetOrNull(
        string targetSectionValue,
        string actionHintValue,
        int? customerId,
        int? orderId,
        string relatedEntityType,
        int? relatedEntityId,
        bool requiresUserAction)
    {
        return TryBuildRequestedTarget(
            targetSectionValue,
            actionHintValue,
            customerId,
            orderId,
            relatedEntityType,
            relatedEntityId,
            requiresUserAction,
            out var requestedTarget,
            out _)
            ? requestedTarget
            : null;
    }

    private async Task<bool> RelatedEntityExistsAsync(NavigationTarget target, CancellationToken cancellationToken)
    {
        if (target.RelatedEntityId is not int relatedEntityId || string.IsNullOrWhiteSpace(target.RelatedEntityType))
        {
            return false;
        }

        if (target.RelatedEntityType.Equals(nameof(Customer), StringComparison.OrdinalIgnoreCase))
        {
            return await _customerRepository.GetByIdAsync(relatedEntityId, cancellationToken) is not null;
        }

        if (target.RelatedEntityType.Equals(nameof(MerchantOrder), StringComparison.OrdinalIgnoreCase)
            || target.RelatedEntityType.Equals("Order", StringComparison.OrdinalIgnoreCase))
        {
            return await _orderRepository.GetByIdAsync(relatedEntityId, cancellationToken) is not null;
        }

        if (target.RelatedEntityType.Equals(nameof(ConversationMessage), StringComparison.OrdinalIgnoreCase))
        {
            return await _messageRepository.GetByIdAsync(relatedEntityId, cancellationToken) is not null;
        }

        if (target.RelatedEntityType.Equals(nameof(AiSuggestion), StringComparison.OrdinalIgnoreCase))
        {
            return await _suggestionRepository.GetByIdAsync(relatedEntityId, cancellationToken) is not null;
        }

        if (target.RelatedEntityType.Equals(nameof(OcrResult), StringComparison.OrdinalIgnoreCase))
        {
            return await _ocrResultRepository.GetByIdAsync(relatedEntityId, cancellationToken) is not null;
        }

        if (target.RelatedEntityType.Equals(nameof(FollowUp), StringComparison.OrdinalIgnoreCase))
        {
            return await _followUpRepository.GetByIdAsync(relatedEntityId, cancellationToken) is not null;
        }

        if (target.RelatedEntityType.Equals(nameof(ActivityLog), StringComparison.OrdinalIgnoreCase))
        {
            return await _activityLogRepository.GetByIdAsync(relatedEntityId, cancellationToken) is not null;
        }

        return false;
    }

    private static NavigationTarget CreateResolvedTarget(NavigationTarget requestedTarget, Customer? customer, MerchantOrder? order)
    {
        return new NavigationTarget
        {
            TargetSection = requestedTarget.TargetSection,
            ActionHint = requestedTarget.ActionHint,
            CustomerId = requestedTarget.CustomerId ?? customer?.Id ?? order?.CustomerId,
            OrderId = requestedTarget.OrderId ?? order?.Id,
            RelatedEntityType = requestedTarget.RelatedEntityType,
            RelatedEntityId = requestedTarget.RelatedEntityId,
            RequiresUserAction = requestedTarget.RequiresUserAction
        };
    }

    private static NavigationTarget? BuildFallbackTarget(Customer? customer, MerchantOrder? order)
    {
        if (order is not null)
        {
            return new NavigationTarget
            {
                TargetSection = NavigationTargetSection.Order,
                ActionHint = NavigationActionHint.OpenOrder,
                CustomerId = order.CustomerId,
                OrderId = order.Id,
                RelatedEntityType = nameof(MerchantOrder),
                RelatedEntityId = order.Id,
                RequiresUserAction = false
            };
        }

        if (customer is not null)
        {
            return new NavigationTarget
            {
                TargetSection = NavigationTargetSection.Customer,
                ActionHint = NavigationActionHint.OpenCustomer,
                CustomerId = customer.Id,
                OrderId = null,
                RelatedEntityType = nameof(Customer),
                RelatedEntityId = customer.Id,
                RequiresUserAction = false
            };
        }

        return null;
    }

    private static NavigationRouteResult Success(
        NavigationTarget resolvedTarget,
        NavigationTarget requestedTarget,
        bool usedFallback,
        string? statusMessage = null)
    {
        return new NavigationRouteResult
        {
            CanNavigate = true,
            RequiresUserAction = requestedTarget.RequiresUserAction,
            UsedFallback = usedFallback,
            DisabledReason = string.Empty,
            StatusMessage = string.IsNullOrWhiteSpace(statusMessage)
                ? requestedTarget.RequiresUserAction
                    ? "路由已解析，需要用户手动确认后执行动作。"
                    : "路由已解析。"
                : statusMessage,
            RequestedTarget = requestedTarget,
            ResolvedTarget = resolvedTarget
        };
    }

    private static NavigationRouteResult Fail(string reason, NavigationTarget? requestedTarget)
    {
        return new NavigationRouteResult
        {
            CanNavigate = false,
            RequiresUserAction = requestedTarget?.RequiresUserAction == true,
            UsedFallback = false,
            DisabledReason = reason,
            StatusMessage = reason,
            RequestedTarget = requestedTarget,
            ResolvedTarget = null
        };
    }
}

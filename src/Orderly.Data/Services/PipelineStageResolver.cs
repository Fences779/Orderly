using Orderly.Core.Models;
using Orderly.Core.Repositories;
using Orderly.Core.Services;

namespace Orderly.Data.Services;

public sealed class PipelineStageResolver : IPipelineStageResolver
{
    private readonly ICustomerRepository _customerRepository;
    private readonly IOrderRepository _orderRepository;
    private readonly IDealRepository _dealRepository;
    private readonly IConversationMessageRepository _messageRepository;
    private readonly IAiSuggestionRepository _suggestionRepository;
    private readonly IFollowUpRepository _followUpRepository;
    private readonly IActivityLogRepository _activityLogRepository;
    private readonly IPriceAdjustmentRepository _priceAdjustmentRepository;

    public PipelineStageResolver(
        ICustomerRepository customerRepository,
        IOrderRepository orderRepository,
        IDealRepository dealRepository,
        IConversationMessageRepository messageRepository,
        IAiSuggestionRepository suggestionRepository,
        IFollowUpRepository followUpRepository,
        IActivityLogRepository activityLogRepository,
        IPriceAdjustmentRepository priceAdjustmentRepository)
    {
        _customerRepository = customerRepository;
        _orderRepository = orderRepository;
        _dealRepository = dealRepository;
        _messageRepository = messageRepository;
        _suggestionRepository = suggestionRepository;
        _followUpRepository = followUpRepository;
        _activityLogRepository = activityLogRepository;
        _priceAdjustmentRepository = priceAdjustmentRepository;
    }

    public async Task<PipelineStageSnapshot> ResolveAsync(int customerId, int? orderId = null, CancellationToken cancellationToken = default)
    {
        var customer = await _customerRepository.GetByIdAsync(customerId, cancellationToken);
        if (customer is null)
        {
            return new PipelineStageSnapshot
            {
                CustomerId = customerId,
                OrderId = orderId,
                Stage = PipelineStage.New,
                Reason = "客户不存在，安全回退到 New。",
                UsedFallback = true,
                ResolvedAt = DateTime.Now
            };
        }

        var orders = await _orderRepository.ListByCustomerIdAsync(customerId, cancellationToken);
        var order = orderId is int selectedOrderId
            ? orders.FirstOrDefault(item => item.Id == selectedOrderId)
            : null;
        var deals = await _dealRepository.ListByCustomerIdAsync(customerId, cancellationToken);
        var messages = await _messageRepository.ListByCustomerIdAsync(customerId, cancellationToken);
        var suggestions = await _suggestionRepository.ListByCustomerIdAsync(customerId, cancellationToken);
        var followUps = await _followUpRepository.ListByCustomerIdAsync(customerId, cancellationToken);
        var activities = await _activityLogRepository.ListByCustomerIdAsync(customerId, cancellationToken);
        var adjustments = await _priceAdjustmentRepository.ListByCustomerIdAsync(customerId, cancellationToken);

        var context = new PipelineStageResolutionContext
        {
            Customer = customer,
            OrderId = orderId,
            Order = order,
            Orders = orders,
            Deals = deals,
            Messages = messages,
            Suggestions = suggestions,
            FollowUps = followUps,
            Activities = activities,
            PriceAdjustments = adjustments
        };

        return PipelineStageRuleEngine.Resolve(context);
    }
}

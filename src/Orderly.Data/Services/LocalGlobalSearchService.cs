using Orderly.Core.Models;
using Orderly.Core.Repositories;
using Orderly.Core.Services;

namespace Orderly.Data.Services;

public sealed partial class LocalGlobalSearchService : IGlobalSearchService
{
    private const int DefaultLimit = 50;
    private const int MaxLimit = 200;
    private const int PrimaryFieldScore = 400;
    private const int ContentFieldScore = 250;
    private const int MetadataFieldScore = 100;

    private readonly ICustomerRepository _customerRepository;
    private readonly IOrderRepository _orderRepository;
    private readonly IDealRepository _dealRepository;
    private readonly IFollowUpRepository _followUpRepository;
    private readonly IConversationMessageRepository _messageRepository;
    private readonly IAiSuggestionRepository _suggestionRepository;
    private readonly IOcrResultRepository _ocrResultRepository;
    private readonly IActivityLogRepository _activityLogRepository;
    private readonly IPriceAdjustmentRepository _priceAdjustmentRepository;

    public LocalGlobalSearchService(
        ICustomerRepository customerRepository,
        IOrderRepository orderRepository,
        IDealRepository dealRepository,
        IFollowUpRepository followUpRepository,
        IConversationMessageRepository messageRepository,
        IAiSuggestionRepository suggestionRepository,
        IOcrResultRepository ocrResultRepository,
        IActivityLogRepository activityLogRepository,
        IPriceAdjustmentRepository priceAdjustmentRepository)
    {
        _customerRepository = customerRepository;
        _orderRepository = orderRepository;
        _dealRepository = dealRepository;
        _followUpRepository = followUpRepository;
        _messageRepository = messageRepository;
        _suggestionRepository = suggestionRepository;
        _ocrResultRepository = ocrResultRepository;
        _activityLogRepository = activityLogRepository;
        _priceAdjustmentRepository = priceAdjustmentRepository;
    }

    public async Task<SearchResultSet> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default)
    {
        var normalizedQuery = NormalizeQuery(request?.Query);
        var limit = ClampLimit(request?.Limit ?? DefaultLimit);
        if (normalizedQuery.Length < 2)
        {
            return new SearchResultSet
            {
                Query = normalizedQuery,
                Limit = limit,
                TotalCount = 0,
                Items = []
            };
        }

        var customers = (await _customerRepository.GetAllAsync(cancellationToken)).ToList();
        var orders = (await _orderRepository.GetRecentAsync(cancellationToken)).ToList();
        var deals = (await _dealRepository.ListAsync(cancellationToken)).ToList();
        var followUps = (await _followUpRepository.ListAsync(cancellationToken)).ToList();
        var messages = (await _messageRepository.ListAsync(cancellationToken)).ToList();
        var suggestions = (await _suggestionRepository.ListAsync(cancellationToken)).ToList();
        var ocrResults = (await _ocrResultRepository.ListAsync(cancellationToken)).ToList();
        var activities = (await _activityLogRepository.ListAsync(cancellationToken)).ToList();
        var priceAdjustments = (await _priceAdjustmentRepository.ListAsync(cancellationToken)).ToList();

        var customerMap = customers.ToDictionary(customer => customer.Id);
        var orderMap = orders.ToDictionary(order => order.Id);
        var results = new List<SearchResultItem>();

        results.AddRange(ProjectCustomers(customers, normalizedQuery));
        results.AddRange(ProjectOrders(orders, normalizedQuery));
        results.AddRange(ProjectConversationMessages(messages, customerMap, orderMap, normalizedQuery));
        results.AddRange(ProjectSuggestions(suggestions, customerMap, orderMap, normalizedQuery));
        results.AddRange(ProjectOcrResults(ocrResults, customerMap, orderMap, normalizedQuery));
        results.AddRange(ProjectFollowUps(followUps, customerMap, orderMap, normalizedQuery));
        results.AddRange(ProjectActivityLogs(activities, customerMap, orderMap, normalizedQuery));

        ProjectionPipelineStageHelper.ApplyToSearchResults(results, customerMap, orders, deals, followUps, messages, suggestions, activities, priceAdjustments);

        var ordered = results
            .OrderBy(item => item, SearchResultComparer.Instance)
            .ToList();

        return new SearchResultSet
        {
            Query = normalizedQuery,
            Limit = limit,
            TotalCount = ordered.Count,
            Items = ordered.Take(limit).ToList()
        };
    }

}

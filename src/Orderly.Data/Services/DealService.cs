using Orderly.Core.Models;
using Orderly.Core.Repositories;
using Orderly.Core.Services;

namespace Orderly.Data.Services;

public sealed class DealService : IDealService
{
    private readonly IDealRepository _dealRepository;
    private readonly IActivityLogRepository _activityLogRepository;

    public DealService(IDealRepository dealRepository, IActivityLogRepository activityLogRepository)
    {
        _dealRepository = dealRepository;
        _activityLogRepository = activityLogRepository;
    }

    public Task<IReadOnlyList<Deal>> GetDealsAsync(CancellationToken cancellationToken = default)
    {
        return _dealRepository.ListAsync(cancellationToken);
    }

    public Task<IReadOnlyList<Deal>> GetCustomerDealsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        return _dealRepository.ListByCustomerIdAsync(customerId, cancellationToken);
    }

    public Task<Deal?> GetDealAsync(int id, CancellationToken cancellationToken = default)
    {
        return _dealRepository.GetByIdAsync(id, cancellationToken);
    }

    public async Task<Deal> SaveDealAsync(Deal deal, CancellationToken cancellationToken = default)
    {
        if (deal.Id <= 0)
        {
            var created = await _dealRepository.CreateAsync(deal, cancellationToken);
            await AddActivityAsync(ActivityType.DealCreated, created.CustomerId, created.Id, null, "创建成交机会", created.Title, cancellationToken);
            return created;
        }

        var existing = await _dealRepository.GetByIdAsync(deal.Id, cancellationToken);
        await _dealRepository.UpdateAsync(deal, cancellationToken);

        if (existing is not null && existing.Stage != deal.Stage)
        {
            await AddActivityAsync(ActivityType.DealStageChanged, deal.CustomerId, deal.Id, null, "更新成交阶段", $"{existing.Stage} -> {deal.Stage}", cancellationToken);
        }

        return deal;
    }

    public async Task UpdateStageAsync(int id, DealStage stage, CancellationToken cancellationToken = default)
    {
        var deal = await _dealRepository.GetByIdAsync(id, cancellationToken);
        if (deal is null)
        {
            return;
        }

        var oldStage = deal.Stage;
        if (oldStage == stage)
        {
            return;
        }

        deal.Stage = stage;
        if (stage == DealStage.Won || stage == DealStage.Lost)
        {
            deal.ClosedAt ??= DateTime.Now;
        }

        await _dealRepository.UpdateAsync(deal, cancellationToken);
        await AddActivityAsync(ActivityType.DealStageChanged, deal.CustomerId, deal.Id, null, "更新成交阶段", $"{oldStage} -> {stage}", cancellationToken);
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
}

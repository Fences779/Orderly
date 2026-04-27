using Orderly.Core.Models;
using Orderly.Core.Repositories;
using Orderly.Core.Services;

namespace Orderly.Data.Services;

public sealed class PriceAdjustmentService : IPriceAdjustmentService
{
    private readonly IPriceAdjustmentRepository _adjustmentRepository;
    private readonly IActivityLogRepository _activityLogRepository;

    public PriceAdjustmentService(IPriceAdjustmentRepository adjustmentRepository, IActivityLogRepository activityLogRepository)
    {
        _adjustmentRepository = adjustmentRepository;
        _activityLogRepository = activityLogRepository;
    }

    public Task<IReadOnlyList<PriceAdjustment>> GetPendingAdjustmentsAsync(CancellationToken cancellationToken = default)
    {
        return _adjustmentRepository.ListPendingAsync(cancellationToken);
    }

    public Task<IReadOnlyList<PriceAdjustment>> GetOrderAdjustmentsAsync(int orderId, CancellationToken cancellationToken = default)
    {
        return _adjustmentRepository.ListByOrderIdAsync(orderId, cancellationToken);
    }

    public Task<IReadOnlyList<PriceAdjustment>> GetCustomerAdjustmentsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        return _adjustmentRepository.ListByCustomerIdAsync(customerId, cancellationToken);
    }

    public async Task<PriceAdjustment> SaveAdjustmentAsync(PriceAdjustment adjustment, CancellationToken cancellationToken = default)
    {
        if (adjustment.Id <= 0)
        {
            var created = await _adjustmentRepository.CreateAsync(adjustment, cancellationToken);
            await AddActivityAsync(ActivityType.PriceAdjustmentRequested, created.CustomerId, created.DealId, created.OrderId, "新增改价申请", created.Reason, cancellationToken);
            return created;
        }

        var existing = await _adjustmentRepository.GetByIdAsync(adjustment.Id, cancellationToken);
        await _adjustmentRepository.UpdateAsync(adjustment, cancellationToken);

        if (existing is not null && existing.Status != adjustment.Status)
        {
            await AddStatusActivityAsync(adjustment, cancellationToken);
        }

        return adjustment;
    }

    public async Task UpdateStatusAsync(int id, PriceAdjustmentStatus status, CancellationToken cancellationToken = default)
    {
        var adjustment = await _adjustmentRepository.GetByIdAsync(id, cancellationToken);
        if (adjustment is null || adjustment.Status == status)
        {
            return;
        }

        adjustment.Status = status;
        if (status == PriceAdjustmentStatus.Approved)
        {
            adjustment.ApprovedAt ??= DateTime.Now;
        }

        await _adjustmentRepository.UpdateAsync(adjustment, cancellationToken);
        await AddStatusActivityAsync(adjustment, cancellationToken);
    }

    private Task AddStatusActivityAsync(PriceAdjustment adjustment, CancellationToken cancellationToken)
    {
        var type = adjustment.Status switch
        {
            PriceAdjustmentStatus.Approved => ActivityType.PriceAdjustmentApproved,
            PriceAdjustmentStatus.Rejected => ActivityType.PriceAdjustmentRejected,
            _ => ActivityType.PriceAdjustmentRequested
        };

        return AddActivityAsync(type, adjustment.CustomerId, adjustment.DealId, adjustment.OrderId, "更新改价状态", adjustment.Status.ToString(), cancellationToken);
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

using Orderly.Core.Models;
using Orderly.Core.Repositories;
using Orderly.Core.Services;

namespace Orderly.Data.Services;

public sealed class PriceAdjustmentService : IPriceAdjustmentService
{
    private const int MaxReasonCharacters = 1000;
    private const int MaxPersonNameCharacters = 80;
    private const int MaxRemoteIdCharacters = 160;
    private const int ActivityDescriptionCharacters = 120;
    private const decimal MaxAdjustmentAmount = 100_000_000m;

    private static readonly DateTime MinAdjustmentDate = new(2000, 1, 1);
    private static readonly DateTime MaxAdjustmentDate = new(2100, 1, 1);

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
        ArgumentNullException.ThrowIfNull(adjustment);

        NormalizeAdjustment(adjustment);
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
        if (!Enum.IsDefined(status))
        {
            throw new InvalidOperationException("改价状态无效。");
        }

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
            Description = BuildActivityDescription(description),
            Operator = "local"
        }, cancellationToken);
    }

    private static void NormalizeAdjustment(PriceAdjustment adjustment)
    {
        if (adjustment.CustomerId <= 0)
        {
            throw new InvalidOperationException("改价申请缺少有效客户。");
        }

        if (adjustment.DealId is <= 0)
        {
            throw new InvalidOperationException("改价申请成交机会无效。");
        }

        if (adjustment.OrderId is <= 0)
        {
            throw new InvalidOperationException("改价申请订单无效。");
        }

        if (!Enum.IsDefined(adjustment.Status))
        {
            throw new InvalidOperationException("改价状态无效。");
        }

        if (adjustment.OriginalAmount < 0 || adjustment.OriginalAmount > MaxAdjustmentAmount)
        {
            throw new InvalidOperationException("改价原价超出允许范围。");
        }

        if (adjustment.AdjustedAmount < 0 || adjustment.AdjustedAmount > MaxAdjustmentAmount)
        {
            throw new InvalidOperationException("改价后价格超出允许范围。");
        }

        EnsureOptionalDateInRange(adjustment.ApprovedAt, "改价审批时间");

        adjustment.Reason = NormalizeRequiredText(adjustment.Reason, MaxReasonCharacters, "改价原因");
        adjustment.RequestedBy = NormalizeOptionalText(adjustment.RequestedBy, MaxPersonNameCharacters, "改价申请人");
        adjustment.ApprovedBy = NormalizeOptionalText(adjustment.ApprovedBy, MaxPersonNameCharacters, "改价审批人");
        adjustment.RemoteId = NormalizeOptionalText(adjustment.RemoteId, MaxRemoteIdCharacters, "改价远端标识");
    }

    private static void EnsureOptionalDateInRange(DateTime? value, string fieldName)
    {
        if (value is null)
        {
            return;
        }

        if (value < MinAdjustmentDate || value > MaxAdjustmentDate)
        {
            throw new InvalidOperationException($"{fieldName}超出允许范围。");
        }
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

    private static string BuildActivityDescription(string description)
    {
        var singleLine = new string((description ?? string.Empty)
            .Select(static ch => ch is '\r' or '\n' or '\t' ? ' ' : ch)
            .ToArray())
            .Trim();

        return singleLine.Length <= ActivityDescriptionCharacters
            ? singleLine
            : $"{singleLine[..ActivityDescriptionCharacters]}...";
    }
}

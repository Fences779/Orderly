using Orderly.Core.Models;
using Orderly.Core.Repositories;
using Orderly.Core.Services;

namespace Orderly.Data.Services;

public sealed class DealService : IDealService
{
    private const int MaxDealTitleCharacters = 120;
    private const int MaxRequirementCharacters = 2000;
    private const int MaxShortFieldCharacters = 80;
    private const int MaxLostReasonCharacters = 1000;
    private const int MaxRemoteIdCharacters = 160;
    private const int ActivityDescriptionCharacters = 120;
    private const decimal MaxDealAmount = 100_000_000m;

    private static readonly DateTime MinDealDate = new(2000, 1, 1);
    private static readonly DateTime MaxDealDate = new(2100, 1, 1);

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
        ArgumentNullException.ThrowIfNull(deal);

        NormalizeDeal(deal);
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
        if (!Enum.IsDefined(stage))
        {
            throw new InvalidOperationException("成交阶段无效。");
        }

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
            Description = BuildActivityDescription(description),
            Operator = "local"
        }, cancellationToken);
    }

    private static void NormalizeDeal(Deal deal)
    {
        if (deal.CustomerId <= 0)
        {
            throw new InvalidOperationException("成交机会缺少有效客户。");
        }

        if (!Enum.IsDefined(deal.Stage))
        {
            throw new InvalidOperationException("成交阶段无效。");
        }

        if (deal.EstimatedAmount < 0 || deal.EstimatedAmount > MaxDealAmount)
        {
            throw new InvalidOperationException("成交金额超出允许范围。");
        }

        EnsureOptionalDateInRange(deal.ExpectedCloseAt, "预计成交时间");
        EnsureOptionalDateInRange(deal.ClosedAt, "成交关闭时间");

        deal.Title = NormalizeRequiredText(deal.Title, MaxDealTitleCharacters, "成交标题");
        deal.Requirement = NormalizeOptionalText(deal.Requirement, MaxRequirementCharacters, "成交需求");
        deal.SourcePlatform = NormalizeOptionalText(deal.SourcePlatform, MaxShortFieldCharacters, "成交来源平台");
        deal.Channel = NormalizeOptionalText(deal.Channel, MaxShortFieldCharacters, "成交渠道");
        deal.LostReason = NormalizeOptionalText(deal.LostReason, MaxLostReasonCharacters, "丢单原因");
        deal.RemoteId = NormalizeOptionalText(deal.RemoteId, MaxRemoteIdCharacters, "成交远端标识");
    }

    private static void EnsureOptionalDateInRange(DateTime? value, string fieldName)
    {
        if (value is null)
        {
            return;
        }

        if (value < MinDealDate || value > MaxDealDate)
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

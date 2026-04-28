using Orderly.Core.Models;
using Orderly.Core.Repositories;
using Orderly.Core.Services;

namespace Orderly.Data.Services;

public sealed class LocalOcrService : IOcrService
{
    private readonly IOcrResultRepository _ocrResultRepository;
    private readonly IActivityLogRepository _activityLogRepository;

    public LocalOcrService(IOcrResultRepository ocrResultRepository, IActivityLogRepository activityLogRepository)
    {
        _ocrResultRepository = ocrResultRepository;
        _activityLogRepository = activityLogRepository;
    }

    public async Task<OcrResult> CreateOcrTaskAsync(OcrResult result, CancellationToken cancellationToken = default)
    {
        result.Status = OcrStatus.Pending;
        result.ExtractedText = string.Empty;
        result.ErrorMessage = string.Empty;

        var created = await _ocrResultRepository.CreateAsync(result, cancellationToken);
        await AddActivityAsync(ActivityType.OcrTaskCreated, created, "创建 OCR 任务", created.SourceName, cancellationToken);
        return created;
    }

    public async Task<OcrResult?> CompleteOcrTaskAsync(int id, string extractedText, CancellationToken cancellationToken = default)
    {
        var result = await _ocrResultRepository.GetByIdAsync(id, cancellationToken);
        if (result is null)
        {
            return null;
        }

        result.Status = OcrStatus.Completed;
        result.ExtractedText = extractedText;
        result.ErrorMessage = string.Empty;
        await _ocrResultRepository.UpdateAsync(result, cancellationToken);
        await AddActivityAsync(ActivityType.OcrTaskCompleted, result, "OCR 完成", result.SourceName, cancellationToken);
        return result;
    }

    public async Task<OcrResult?> FailOcrTaskAsync(int id, string errorMessage, CancellationToken cancellationToken = default)
    {
        var result = await _ocrResultRepository.GetByIdAsync(id, cancellationToken);
        if (result is null)
        {
            return null;
        }

        result.Status = OcrStatus.Failed;
        result.ErrorMessage = errorMessage;
        await _ocrResultRepository.UpdateAsync(result, cancellationToken);
        await AddActivityAsync(ActivityType.OcrTaskFailed, result, "OCR 失败", errorMessage, cancellationToken);
        return result;
    }

    public Task<IReadOnlyList<OcrResult>> ListByCustomerAsync(int customerId, CancellationToken cancellationToken = default)
    {
        return _ocrResultRepository.ListByCustomerIdAsync(customerId, cancellationToken);
    }

    private Task AddActivityAsync(ActivityType type, OcrResult result, string title, string description, CancellationToken cancellationToken)
    {
        return _activityLogRepository.CreateAsync(new ActivityLog
        {
            Type = type,
            CustomerId = result.CustomerId,
            OrderId = result.OrderId,
            Title = title,
            Description = description,
            Operator = "local-stub"
        }, cancellationToken);
    }
}

using Orderly.Core.Models;
using Orderly.Core.Repositories;
using Orderly.Core.Services;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Orderly.Data.Services;

public sealed class LocalOcrService : IOcrService
{
    private readonly IOcrResultRepository _ocrResultRepository;
    private readonly IActivityLogRepository _activityLogRepository;
    private readonly IConversationService _conversationService;
    private readonly IConversationMessageRepository _messageRepository;

    public LocalOcrService(
        IOcrResultRepository ocrResultRepository,
        IActivityLogRepository activityLogRepository,
        IConversationService conversationService,
        IConversationMessageRepository messageRepository)
    {
        _ocrResultRepository = ocrResultRepository;
        _activityLogRepository = activityLogRepository;
        _conversationService = conversationService;
        _messageRepository = messageRepository;
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
        var metadata = ParseMetadata(result.MetadataJson);
        metadata["errorSummary"] = errorMessage;
        result.MetadataJson = metadata.ToJsonString();
        await _ocrResultRepository.UpdateAsync(result, cancellationToken);
        await AddActivityAsync(ActivityType.OcrTaskFailed, result, "OCR 失败", errorMessage, cancellationToken);
        return result;
    }

    public Task<IReadOnlyList<OcrResult>> ListByCustomerAsync(int customerId, CancellationToken cancellationToken = default)
    {
        return _ocrResultRepository.ListByCustomerIdAsync(customerId, cancellationToken);
    }

    public async Task<ConversationMessage> ConvertToConversationMessageAsync(
        int ocrResultId,
        string senderName,
        int? dealId = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _ocrResultRepository.GetByIdAsync(ocrResultId, cancellationToken)
            ?? throw new InvalidOperationException($"OCR 结果不存在：{ocrResultId}。");

        if (result.CustomerId is null or <= 0)
        {
            throw new InvalidOperationException("OCR 结果缺少 CustomerId，无法转为沟通记录。");
        }

        if (result.Status != OcrStatus.Completed)
        {
            throw new InvalidOperationException("只有已完成的 OCR 结果才能转为沟通记录。");
        }

        if (string.IsNullOrWhiteSpace(result.ExtractedText))
        {
            throw new InvalidOperationException("OCR 文本为空，无法转为沟通记录。");
        }

        var sourceMessageId = BuildSourceMessageId(result.Id);
        var existingMessage = await _messageRepository.GetBySourceMessageIdAsync(sourceMessageId, cancellationToken);
        if (existingMessage is not null)
        {
            await PersistConvertedMessageIdAsync(result, existingMessage.Id, cancellationToken);
            return existingMessage;
        }

        var metadata = ParseMetadata(result.MetadataJson);
        var created = await _conversationService.SaveMessageAsync(new ConversationMessage
        {
            CustomerId = result.CustomerId.Value,
            OrderId = result.OrderId,
            DealId = dealId,
            Direction = MessageDirection.Incoming,
            Channel = MessageChannel.Manual,
            SenderName = string.IsNullOrWhiteSpace(senderName) ? "截图导入" : senderName.Trim(),
            Content = result.ExtractedText.Trim(),
            SourceMessageId = sourceMessageId,
            MetadataJson = BuildConversationMetadataJson(result, metadata)
        }, cancellationToken);

        await PersistConvertedMessageIdAsync(result, created.Id, cancellationToken);
        return created;
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

    private async Task PersistConvertedMessageIdAsync(OcrResult result, int messageId, CancellationToken cancellationToken)
    {
        var metadata = ParseMetadata(result.MetadataJson);
        var currentId = metadata["convertedToMessageId"]?.GetValue<int?>();
        if (currentId == messageId)
        {
            return;
        }

        metadata["convertedToMessageId"] = messageId;
        result.MetadataJson = metadata.ToJsonString();
        await _ocrResultRepository.UpdateAsync(result, cancellationToken);
    }

    private static string BuildSourceMessageId(int ocrResultId)
    {
        return $"ocr-result:{ocrResultId}";
    }

    private static string BuildConversationMetadataJson(OcrResult result, JsonObject ocrMetadata)
    {
        return JsonSerializer.Serialize(new
        {
            source = ocrMetadata["source"]?.GetValue<string>() ?? "manual-image",
            createdBy = "p2.5",
            ocrResultId = result.Id,
            provider = ocrMetadata["provider"]?.GetValue<string>() ?? "local",
            usedFallback = ocrMetadata["usedFallback"]?.GetValue<bool>() ?? true,
            fileName = ocrMetadata["fileName"]?.GetValue<string>() ?? result.SourceName
        });
    }

    private static JsonObject ParseMetadata(string metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return new JsonObject();
        }

        try
        {
            return JsonNode.Parse(metadataJson) as JsonObject ?? new JsonObject();
        }
        catch (JsonException)
        {
            return new JsonObject
            {
                ["legacyMetadata"] = metadataJson
            };
        }
    }
}

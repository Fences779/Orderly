using Orderly.Core.Models;
using Orderly.Core.Repositories;
using Orderly.Core.Services;
using Orderly.Data.Sqlite;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Orderly.Data.Services;

public sealed class LocalOcrService : IOcrService
{
    private const long MaxOcrSourceFileBytes = 20L * 1024L * 1024L;
    private const int MaxOcrSourcePathCharacters = 1024;
    private const int MaxOcrSourceNameCharacters = 160;
    private const int MaxOcrExtractedTextCharacters = 10000;
    private const int MaxOcrErrorMessageCharacters = 512;
    private const int MaxOcrMetadataJsonCharacters = 4096;
    private const int MaxOcrMetadataScalarCharacters = 160;

    private static readonly JsonDocumentOptions OcrMetadataJsonDocumentOptions = new()
    {
        MaxDepth = 16
    };

    private static readonly HashSet<string> AllowedOcrImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png",
        ".jpg",
        ".jpeg",
        ".bmp",
        ".gif",
        ".webp"
    };

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
        ArgumentNullException.ThrowIfNull(result);

        NormalizeOcrTaskInput(result);
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

        // 状态机门控：仅允许从 Pending 进入终态。若已处于终态（Completed/Failed），
        // 拒绝非法转换并幂等返回当前终态记录，不覆盖 Status/ExtractedText/MetadataJson。
        if (result.Status != OcrStatus.Pending)
        {
            return result;
        }

        result.Status = OcrStatus.Completed;
        result.ExtractedText = NormalizeOcrText(extractedText, MaxOcrExtractedTextCharacters, "OCR 文本");
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

        // 状态机门控：仅允许从 Pending 进入终态。若已处于终态（Completed/Failed），
        // 拒绝非法转换并幂等返回当前终态记录，不覆盖 Status/ExtractedText/MetadataJson。
        if (result.Status != OcrStatus.Pending)
        {
            return result;
        }

        result.Status = OcrStatus.Failed;
        var normalizedErrorMessage = NormalizeOcrText(errorMessage, MaxOcrErrorMessageCharacters, "OCR 错误信息");
        result.ErrorMessage = normalizedErrorMessage;
        var metadata = ParseMetadata(result.MetadataJson);
        metadata["errorSummary"] = normalizedErrorMessage;
        result.MetadataJson = metadata.ToJsonString();
        await _ocrResultRepository.UpdateAsync(result, cancellationToken);
        await AddActivityAsync(ActivityType.OcrTaskFailed, result, "OCR 失败", normalizedErrorMessage, cancellationToken);
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

    private static void NormalizeOcrTaskInput(OcrResult result)
    {
        result.SourcePath = NormalizeOcrSourcePath(result.SourcePath);
        result.SourceName = NormalizeOcrSourceName(result.SourceName, result.SourcePath);
        result.MetadataJson = NormalizeOcrText(result.MetadataJson, MaxOcrMetadataJsonCharacters, "OCR 元数据");
    }

    private static string NormalizeOcrSourcePath(string? sourcePath)
    {
        var trimmed = sourcePath?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new InvalidOperationException("OCR 图片路径不能为空。");
        }

        if (trimmed.Length > MaxOcrSourcePathCharacters
            || trimmed.Any(char.IsControl))
        {
            throw new InvalidOperationException("OCR 图片路径长度超限或包含控制字符。");
        }

        var fullPath = Path.GetFullPath(trimmed);
        if (fullPath.StartsWith(@"\\", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("OCR 图片不能来自网络共享路径。");
        }

        var extension = Path.GetExtension(fullPath);
        if (!AllowedOcrImageExtensions.Contains(extension))
        {
            throw new InvalidOperationException("OCR 仅支持 PNG、JPG、JPEG、BMP、GIF 或 WEBP 图片。");
        }

        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            LocalDataFileSecurity.EnsureDirectoryIsNotLinked(directory, "OCR 图片目录");
        }

        if (LocalDataFileSecurity.IsReparsePoint(fullPath))
        {
            throw new InvalidOperationException("OCR 图片文件不能是链接文件。");
        }

        if (File.Exists(fullPath))
        {
            var fileInfo = new FileInfo(fullPath);
            if (fileInfo.Length > MaxOcrSourceFileBytes)
            {
                throw new InvalidOperationException($"OCR 图片不能超过 {MaxOcrSourceFileBytes / 1024L / 1024L} MB。");
            }
        }

        return fullPath;
    }

    private static string NormalizeOcrSourceName(string? sourceName, string sourcePath)
    {
        var normalized = Path.GetFileName(sourceName);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            normalized = Path.GetFileName(sourcePath);
        }

        normalized = normalized.Trim();
        if (string.IsNullOrWhiteSpace(normalized)
            || normalized.Length > MaxOcrSourceNameCharacters
            || normalized.Any(char.IsControl))
        {
            throw new InvalidOperationException("OCR 图片文件名无效。");
        }

        return normalized;
    }

    private static string NormalizeOcrText(string? value, int maxCharacters, string fieldName)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length > maxCharacters
            || normalized.Any(static ch => char.IsControl(ch) && ch is not '\r' and not '\n' and not '\t'))
        {
            throw new InvalidOperationException($"{fieldName}长度超限或包含控制字符。");
        }

        return normalized;
    }

    private async Task PersistConvertedMessageIdAsync(OcrResult result, int messageId, CancellationToken cancellationToken)
    {
        var metadata = ParseMetadata(result.MetadataJson);
        var currentId = ReadMetadataInt(metadata, "convertedToMessageId");
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
            source = ReadMetadataString(ocrMetadata, "source", "manual-image"),
            createdBy = "p2.5",
            ocrResultId = result.Id,
            provider = ReadMetadataString(ocrMetadata, "provider", "local"),
            usedFallback = ReadMetadataBool(ocrMetadata, "usedFallback", fallback: true),
            fileName = ReadMetadataString(ocrMetadata, "fileName", result.SourceName)
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
            return JsonNode.Parse(metadataJson, documentOptions: OcrMetadataJsonDocumentOptions) as JsonObject ?? new JsonObject();
        }
        catch (JsonException)
        {
            return new JsonObject
            {
                ["legacyMetadata"] = metadataJson
            };
        }
    }

    private static string ReadMetadataString(JsonObject metadata, string name, string fallback)
    {
        if (!metadata.TryGetPropertyValue(name, out var node) || node is null)
        {
            return fallback;
        }

        try
        {
            var value = NormalizeOcrText(node.GetValue<string>(), MaxOcrMetadataScalarCharacters, $"OCR 元数据 {name}");
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException)
        {
            return fallback;
        }
    }

    private static bool ReadMetadataBool(JsonObject metadata, string name, bool fallback)
    {
        if (!metadata.TryGetPropertyValue(name, out var node) || node is null)
        {
            return fallback;
        }

        try
        {
            return node.GetValue<bool>();
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException)
        {
            return fallback;
        }
    }

    private static int? ReadMetadataInt(JsonObject metadata, string name)
    {
        if (!metadata.TryGetPropertyValue(name, out var node) || node is null)
        {
            return null;
        }

        try
        {
            return node.GetValue<int>();
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException)
        {
            return null;
        }
    }
}

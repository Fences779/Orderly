using Orderly.Core.Models;
using Orderly.Core.Repositories;
using Orderly.Core.Services;

namespace Orderly.Data.Services;

public sealed class NoteService : INoteService
{
    private const int MaxNoteContentCharacters = 4000;
    private const int MaxActivityMetadataCharacters = 4096;
    private const int ActivityDescriptionCharacters = 120;

    private readonly ICustomerNoteRepository _noteRepository;
    private readonly IActivityLogRepository _activityLogRepository;

    public NoteService(ICustomerNoteRepository noteRepository, IActivityLogRepository activityLogRepository)
    {
        _noteRepository = noteRepository;
        _activityLogRepository = activityLogRepository;
    }

    public Task<IReadOnlyList<CustomerNote>> GetNotesAsync(CancellationToken cancellationToken = default)
    {
        return _noteRepository.ListAsync(cancellationToken);
    }

    public Task<IReadOnlyList<CustomerNote>> GetCustomerNotesAsync(int customerId, CancellationToken cancellationToken = default)
    {
        return _noteRepository.ListByCustomerIdAsync(customerId, cancellationToken);
    }

    public Task<CustomerNote?> GetNoteAsync(int id, CancellationToken cancellationToken = default)
    {
        return _noteRepository.GetByIdAsync(id, cancellationToken);
    }

    public async Task<CustomerNote> SaveNoteAsync(CustomerNote note, string activityMetadataJson = "", CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(note);

        NormalizeNote(note);
        activityMetadataJson = NormalizeOptionalText(activityMetadataJson, MaxActivityMetadataCharacters, "备注活动元数据", allowLineBreaks: false);
        if (note.Id <= 0)
        {
            var created = await _noteRepository.CreateAsync(note, cancellationToken);
            await AddActivityAsync(ActivityType.NoteCreated, created.CustomerId, created.DealId, created.OrderId, "新增客户备注", created.Content, activityMetadataJson, cancellationToken);
            return created;
        }

        await _noteRepository.UpdateAsync(note, cancellationToken);
        return note;
    }

    public Task DeleteNoteAsync(int id, CancellationToken cancellationToken = default)
    {
        return _noteRepository.SoftDeleteAsync(id, cancellationToken);
    }

    private Task AddActivityAsync(ActivityType type, int? customerId, int? dealId, int? orderId, string title, string description, string metadataJson, CancellationToken cancellationToken)
    {
        return _activityLogRepository.CreateAsync(new ActivityLog
        {
            Type = type,
            CustomerId = customerId,
            DealId = dealId,
            OrderId = orderId,
            Title = title,
            Description = BuildActivityDescription(description),
            Operator = "local",
            MetadataJson = metadataJson
        }, cancellationToken);
    }

    private static void NormalizeNote(CustomerNote note)
    {
        if (note.CustomerId <= 0)
        {
            throw new InvalidOperationException("客户备注缺少有效客户。");
        }

        if (!Enum.IsDefined(note.Type))
        {
            throw new InvalidOperationException("客户备注类型无效。");
        }

        note.Content = NormalizeRequiredText(note.Content, MaxNoteContentCharacters, "客户备注内容", allowLineBreaks: true);
    }

    private static string NormalizeRequiredText(string? value, int maxCharacters, string fieldName, bool allowLineBreaks)
    {
        var normalized = NormalizeOptionalText(value, maxCharacters, fieldName, allowLineBreaks);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException($"{fieldName}不能为空。");
        }

        return normalized;
    }

    private static string NormalizeOptionalText(string? value, int maxCharacters, string fieldName, bool allowLineBreaks)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length > maxCharacters)
        {
            throw new InvalidOperationException($"{fieldName}不能超过 {maxCharacters} 个字符。");
        }

        if (normalized.Any(ch => char.IsControl(ch) && !(allowLineBreaks && ch is '\r' or '\n' or '\t')))
        {
            throw new InvalidOperationException($"{fieldName}不能包含控制字符。");
        }

        return normalized;
    }

    private static string BuildActivityDescription(string content)
    {
        var singleLine = new string(content
            .Select(static ch => ch is '\r' or '\n' or '\t' ? ' ' : ch)
            .ToArray())
            .Trim();

        return singleLine.Length <= ActivityDescriptionCharacters
            ? singleLine
            : $"{singleLine[..ActivityDescriptionCharacters]}...";
    }
}

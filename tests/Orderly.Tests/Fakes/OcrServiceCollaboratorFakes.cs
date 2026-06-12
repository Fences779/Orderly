using Orderly.Core.Models;
using Orderly.Core.Repositories;
using Orderly.Core.Services;

namespace Orderly.Tests.Fakes;

/// <summary>
/// 记录写入的活动日志，用于驱动 <see cref="Orderly.Data.Services.LocalOcrService"/>
/// 终态变更路径所需的 <see cref="IActivityLogRepository.CreateAsync"/> 调用。
/// </summary>
internal sealed class RecordingActivityLogRepository : IActivityLogRepository
{
    public List<ActivityLog> Created { get; } = new();

    public Task<ActivityLog> CreateAsync(ActivityLog activityLog, CancellationToken cancellationToken = default)
    {
        activityLog.Id = Created.Count + 1;
        Created.Add(activityLog);
        return Task.FromResult(activityLog);
    }

    public Task<ActivityLog?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
        => Task.FromResult(Created.FirstOrDefault(a => a.Id == id));

    public Task<IReadOnlyList<ActivityLog>> ListAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<ActivityLog>>(Created.ToList());

    public Task<IReadOnlyList<ActivityLog>> ListRecentAsync(int count, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<ActivityLog>>(Created.TakeLast(count).ToList());

    public Task<IReadOnlyList<ActivityLog>> ListByCustomerIdAsync(int customerId, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<ActivityLog>>(Created.Where(a => a.CustomerId == customerId).ToList());

    public Task<int> SoftDeleteOlderThanAsync(DateTime cutoff, CancellationToken cancellationToken = default)
        => Task.FromResult(0);

    public Task UpdateAsync(ActivityLog activityLog, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task SoftDeleteAsync(int id, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

/// <summary>
/// OCR 终态变更路径不消费会话服务，仅在构造函数依赖处占位。
/// </summary>
internal sealed class NoOpConversationService : IConversationService
{
    public Task<ConversationMessage> SaveMessageAsync(ConversationMessage message, CancellationToken cancellationToken = default)
        => Task.FromResult(message);

    public Task<IReadOnlyList<ConversationMessage>> ListByCustomerAsync(int customerId, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<ConversationMessage>>(Array.Empty<ConversationMessage>());

    public Task<IReadOnlyList<ConversationMessage>> ListByOrderAsync(int orderId, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<ConversationMessage>>(Array.Empty<ConversationMessage>());
}

/// <summary>
/// OCR 终态变更路径不消费会话消息仓储，仅在构造函数依赖处占位。
/// </summary>
internal sealed class NoOpConversationMessageRepository : IConversationMessageRepository
{
    public Task<ConversationMessage> CreateAsync(ConversationMessage message, CancellationToken cancellationToken = default)
        => Task.FromResult(message);

    public Task UpdateAsync(ConversationMessage message, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<ConversationMessage?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
        => Task.FromResult<ConversationMessage?>(null);

    public Task<ConversationMessage?> GetBySourceMessageIdAsync(string sourceMessageId, CancellationToken cancellationToken = default)
        => Task.FromResult<ConversationMessage?>(null);

    public Task<IReadOnlyList<ConversationMessage>> ListAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<ConversationMessage>>(Array.Empty<ConversationMessage>());

    public Task<IReadOnlyList<ConversationMessage>> ListByCustomerIdAsync(int customerId, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<ConversationMessage>>(Array.Empty<ConversationMessage>());

    public Task<IReadOnlyList<ConversationMessage>> ListByOrderIdAsync(int orderId, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<ConversationMessage>>(Array.Empty<ConversationMessage>());
}

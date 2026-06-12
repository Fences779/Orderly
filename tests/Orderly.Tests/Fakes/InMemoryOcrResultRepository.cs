using Orderly.Core.Models;
using Orderly.Core.Repositories;

namespace Orderly.Tests.Fakes;

/// <summary>
/// 简单的内存 OCR 结果仓储，用于在不触碰磁盘/SQLite 的前提下驱动
/// <see cref="Orderly.Data.Services.LocalOcrService"/> 的状态变更路径
/// （<c>CompleteOcrTaskAsync</c> / <c>FailOcrTaskAsync</c>）。
///
/// 读写均做克隆隔离：<see cref="GetByIdAsync"/> 返回独立副本，
/// <see cref="UpdateAsync"/> 提交独立副本，确保测试可对"提交后"的状态做快照比对，
/// 不受服务内部就地修改返回对象的影响。
/// </summary>
internal sealed class InMemoryOcrResultRepository : IOcrResultRepository
{
    private readonly Dictionary<int, OcrResult> _store = new();
    private int _nextId = 1;

    public Task<OcrResult> CreateAsync(OcrResult result, CancellationToken cancellationToken = default)
    {
        if (result.Id == 0)
        {
            result.Id = _nextId++;
        }
        else if (result.Id >= _nextId)
        {
            _nextId = result.Id + 1;
        }

        _store[result.Id] = Clone(result);
        return Task.FromResult(Clone(result));
    }

    public Task UpdateAsync(OcrResult result, CancellationToken cancellationToken = default)
    {
        _store[result.Id] = Clone(result);
        return Task.CompletedTask;
    }

    public Task<OcrResult?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
        => Task.FromResult(_store.TryGetValue(id, out var stored) ? Clone(stored) : null);

    public Task<IReadOnlyList<OcrResult>> ListAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<OcrResult>>(_store.Values.Select(Clone).ToList());

    public Task<IReadOnlyList<OcrResult>> ListByCustomerIdAsync(int customerId, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<OcrResult>>(
            _store.Values.Where(r => r.CustomerId == customerId).Select(Clone).ToList());

    /// <summary>
    /// 直接植入一条记录（绕过 <c>CreateOcrTaskAsync</c> 的输入归一化/路径校验），
    /// 便于测试以确定的初始状态（如 <see cref="OcrStatus.Pending"/>）起步。
    /// </summary>
    public OcrResult Seed(OcrResult result)
    {
        if (result.Id == 0)
        {
            result.Id = _nextId++;
        }
        else if (result.Id >= _nextId)
        {
            _nextId = result.Id + 1;
        }

        _store[result.Id] = Clone(result);
        return Clone(result);
    }

    private static OcrResult Clone(OcrResult source) => new()
    {
        Id = source.Id,
        CustomerId = source.CustomerId,
        OrderId = source.OrderId,
        SourcePath = source.SourcePath,
        SourceName = source.SourceName,
        ExtractedText = source.ExtractedText,
        Status = source.Status,
        ErrorMessage = source.ErrorMessage,
        MetadataJson = source.MetadataJson,
        CreatedAt = source.CreatedAt,
        UpdatedAt = source.UpdatedAt,
        DeletedAt = source.DeletedAt,
        RemoteId = source.RemoteId,
        IsSynced = source.IsSynced,
        Version = source.Version,
    };
}

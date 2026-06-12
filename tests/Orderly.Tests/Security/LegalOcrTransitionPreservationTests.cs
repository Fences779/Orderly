using CsCheck;
using Orderly.Core.Models;
using Orderly.Data.Services;
using Orderly.Tests.Fakes;
using Xunit;

namespace Orderly.Tests.Security;

/// <summary>
/// Property 6 (Preservation) — 合法 OCR 转换与正常路径保持不变。
///
/// 本测试遵循"观察优先（observation-first）"方法学：在**未修复代码**上观察非漏洞输入
/// （<c>isBugCondition</c> 返回 false —— 即从 <see cref="OcrStatus.Pending"/> 出发的合法终态转换）
/// 的可观察行为，并将其固化为属性测试，作为修复（任务 7.2）必须保持的回归防护契约。
///
/// 对应 design.md：
///   - Bug Condition CASE "OcrTerminalTransition" 仅在 currentStatus ∈ [Completed, Failed] 时为 true；
///     因此"从 Pending 出发的 Complete / Fail"恒为 ¬isBugCondition（非漏洞输入）。
///   - Preservation Checking：FOR ALL input WHERE NOT isBugCondition(input):
///                            originalFunction(input) == fixedFunction(input)
///
/// **观察到的基线行为（未修复代码）**：
///   1. <c>CompleteOcrTaskAsync(pendingId, text)</c>：
///        Status → Completed；ExtractedText → text.Trim()（归一化）；ErrorMessage → 空；
///        MetadataJson 不变；写入一条 <see cref="ActivityType.OcrTaskCompleted"/> 活动日志；返回非空记录。
///   2. <c>FailOcrTaskAsync(pendingId, error)</c>：
///        Status → Failed；ErrorMessage → error.Trim()；MetadataJson 增加 "errorSummary"；
///        写入一条 <see cref="ActivityType.OcrTaskFailed"/> 活动日志；返回非空记录。
///   3. 对不存在的 id，两个方法都返回 null（不抛异常、不写日志）。
///
/// **EXPECTED OUTCOME**: 在未修复代码上 PASS（确认需要保持的基线行为）。
///
/// **Validates: Requirements 3.10**
/// </summary>
public sealed class LegalOcrTransitionPreservationTests
{
    // 安全文本：字母与空格混合，可触发首尾空白的归一化（Trim），但不含会被拒绝的控制字符。
    private static readonly Gen<string> TextWithWhitespaceGen =
        Gen.OneOf(Gen.Const(' '), Gen.Char['a', 'z']).Array[0, 40]
            .Select(chars => new string(chars));

    /// <summary>
    /// Property 6a — 从 Pending 出发的 CompleteOcrTaskAsync 行为保持不变。
    /// </summary>
    [Fact]
    public void Pending_to_completed_transition_preserves_observed_behavior()
    {
        TextWithWhitespaceGen.Sample(rawText =>
        {
            var repo = new InMemoryOcrResultRepository();
            var activityLog = new RecordingActivityLogRepository();
            var service = NewService(repo, activityLog);
            var seeded = SeedPending(repo);
            var metadataBefore = seeded.MetadataJson;

            var returned = service.CompleteOcrTaskAsync(seeded.Id, rawText).GetAwaiter().GetResult();
            var stored = repo.GetByIdAsync(seeded.Id).GetAwaiter().GetResult();

            Assert.NotNull(returned);
            Assert.NotNull(stored);

            // 合法转换：终态 Completed。
            Assert.Equal(OcrStatus.Completed, stored!.Status);
            // 文本归一化：Trim。
            Assert.Equal(rawText.Trim(), stored.ExtractedText);
            // 错误信息清空。
            Assert.Equal(string.Empty, stored.ErrorMessage);
            // MetadataJson 在完成路径上不变。
            Assert.Equal(metadataBefore, stored.MetadataJson);
            // 返回值与已提交记录一致。
            Assert.Equal(stored.Status, returned!.Status);
            Assert.Equal(stored.ExtractedText, returned.ExtractedText);
            // 活动日志：恰写入一条 OcrTaskCompleted。
            Assert.Single(activityLog.Created);
            Assert.Equal(ActivityType.OcrTaskCompleted, activityLog.Created[0].Type);
        });
    }

    /// <summary>
    /// Property 6b — 从 Pending 出发的 FailOcrTaskAsync 行为保持不变。
    /// </summary>
    [Fact]
    public void Pending_to_failed_transition_preserves_observed_behavior()
    {
        TextWithWhitespaceGen.Sample(rawError =>
        {
            var repo = new InMemoryOcrResultRepository();
            var activityLog = new RecordingActivityLogRepository();
            var service = NewService(repo, activityLog);
            var seeded = SeedPending(repo);

            var returned = service.FailOcrTaskAsync(seeded.Id, rawError).GetAwaiter().GetResult();
            var stored = repo.GetByIdAsync(seeded.Id).GetAwaiter().GetResult();

            Assert.NotNull(returned);
            Assert.NotNull(stored);

            // 合法转换：终态 Failed。
            Assert.Equal(OcrStatus.Failed, stored!.Status);
            // 错误信息归一化：Trim。
            Assert.Equal(rawError.Trim(), stored.ErrorMessage);
            // MetadataJson 写入 errorSummary。
            Assert.Contains("errorSummary", stored.MetadataJson);
            // 活动日志：恰写入一条 OcrTaskFailed。
            Assert.Single(activityLog.Created);
            Assert.Equal(ActivityType.OcrTaskFailed, activityLog.Created[0].Type);
        });
    }

    /// <summary>
    /// Property 6c — 对不存在的 id，Complete/Fail 均返回 null 且不写活动日志（保持基线）。
    /// </summary>
    [Fact]
    public void Missing_task_returns_null_for_both_terminal_methods()
    {
        Gen.Int[10_000, 1_000_000].Sample(missingId =>
        {
            var repo = new InMemoryOcrResultRepository();
            var activityLog = new RecordingActivityLogRepository();
            var service = NewService(repo, activityLog);

            var completed = service.CompleteOcrTaskAsync(missingId, "text").GetAwaiter().GetResult();
            var failed = service.FailOcrTaskAsync(missingId, "error").GetAwaiter().GetResult();

            Assert.Null(completed);
            Assert.Null(failed);
            Assert.Empty(activityLog.Created);
        });
    }

    private static LocalOcrService NewService(
        InMemoryOcrResultRepository repo,
        RecordingActivityLogRepository activityLog)
        => new(
            repo,
            activityLog,
            new NoOpConversationService(),
            new NoOpConversationMessageRepository());

    private static OcrResult SeedPending(InMemoryOcrResultRepository repo)
        => repo.Seed(new OcrResult
        {
            CustomerId = 1,
            SourcePath = @"C:\images\seed.png",
            SourceName = "seed.png",
            Status = OcrStatus.Pending,
            ExtractedText = string.Empty,
            ErrorMessage = string.Empty,
            MetadataJson = string.Empty,
        });
}

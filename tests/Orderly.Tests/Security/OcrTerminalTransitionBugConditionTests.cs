using CsCheck;
using Orderly.Core.Models;
using Orderly.Data.Services;
using Orderly.Tests.Fakes;
using Xunit;

namespace Orderly.Tests.Security;

/// <summary>
/// Property 2 (Bug Condition) — OCR 终态状态机门控。
///
/// 本测试编码了"修复后"的期望行为（design.md Property 2 / Requirements 2.2）：
///   对已处于终态（<see cref="OcrStatus.Completed"/> / <see cref="OcrStatus.Failed"/>）的任务，
///   再次调用 <c>CompleteOcrTaskAsync</c> / <c>FailOcrTaskAsync</c> 时 SHALL 依据状态机规则
///   拒绝非法转换（保持幂等或返回受控失败），不得无条件覆盖终态任务的
///   <c>Status</c> / <c>ExtractedText</c> / <c>MetadataJson</c>。
///
/// 对应 design.md Bug Condition：
///   isBugCondition CASE "OcrTerminalTransition"
///     = currentStatus(input.taskId) IN [Completed, Failed]
///       AND input.requestedTransition IN [Complete, Fail]
///
/// 具体反例（来自 tasks.md / design.md Examples）：
///   - 先 CompleteOcrTaskAsync 再 FailOcrTaskAsync（Completed → Failed）
///   - 先 FailOcrTaskAsync 再 CompleteOcrTaskAsync（Failed → Completed）
///
/// **CRITICAL**: 在未修复代码上本测试预期 FAIL（失败即确认缺陷存在：终态被覆盖、文本被改写）。
/// 根因：<c>CompleteOcrTaskAsync</c> / <c>FailOcrTaskAsync</c> 取出记录后无条件赋值新状态，
/// 缺少基于 <see cref="OcrStatus"/> 的转换合法性判定。
///
/// **Validates: Requirements 2.2**
/// </summary>
public sealed class OcrTerminalTransitionBugConditionTests
{
    // 安全文本生成器：仅含可打印字母数字，避免触发 LocalOcrService 的归一化校验
    // （控制字符/超长会抛 InvalidOperationException，与本属性无关）。
    private static readonly Gen<string> SafeTextGen =
        Gen.Char['a', 'z'].Array[0, 40].Select(chars => new string(chars));

    // true = Complete（终态 Completed）；false = Fail（终态 Failed）。
    private static readonly Gen<bool> IsCompleteGen = Gen.Bool;

    /// <summary>
    /// Property 2 — 终态不可被非法翻转。
    ///
    /// 对任意"首个合法终态转换（来自 Pending）"与"随后的终态→终态请求"组合，
    /// 第二次调用后任务的 Status / ExtractedText / MetadataJson 必须与第一次终态后一致。
    /// 覆盖全部 2×2 组合，含两个具体反例：Completed→Failed 与 Failed→Completed。
    /// </summary>
    [Fact]
    public void Terminal_ocr_tasks_cannot_be_illegally_flipped()
    {
        var gen =
            from firstIsComplete in IsCompleteGen
            from secondIsComplete in IsCompleteGen
            from firstText in SafeTextGen
            from secondText in SafeTextGen
            select (firstIsComplete, secondIsComplete, firstText, secondText);

        gen.Sample(input =>
        {
            var (firstIsComplete, secondIsComplete, firstText, secondText) = input;

            var repo = new InMemoryOcrResultRepository();
            var service = new LocalOcrService(
                repo,
                new RecordingActivityLogRepository(),
                new NoOpConversationService(),
                new NoOpConversationMessageRepository());

            // 以确定的 Pending 任务起步（绕过 CreateOcrTaskAsync 的路径校验）。
            var seeded = repo.Seed(new OcrResult
            {
                CustomerId = 1,
                SourcePath = @"C:\images\seed.png",
                SourceName = "seed.png",
                Status = OcrStatus.Pending,
                ExtractedText = string.Empty,
                ErrorMessage = string.Empty,
                MetadataJson = string.Empty,
            });

            // 第一次：Pending → 终态（合法转换）。
            _ = firstIsComplete
                ? service.CompleteOcrTaskAsync(seeded.Id, firstText).GetAwaiter().GetResult()
                : service.FailOcrTaskAsync(seeded.Id, firstText).GetAwaiter().GetResult();

            var afterFirst = repo.GetByIdAsync(seeded.Id).GetAwaiter().GetResult();
            Assert.NotNull(afterFirst);

            // 第二次：终态 → 终态（非法转换，应被拒绝/幂等）。
            try
            {
                _ = secondIsComplete
                    ? service.CompleteOcrTaskAsync(seeded.Id, secondText).GetAwaiter().GetResult()
                    : service.FailOcrTaskAsync(seeded.Id, secondText).GetAwaiter().GetResult();
            }
            catch (InvalidOperationException)
            {
                // 受控失败也是可接受的拒绝语义；状态仍不应被破坏，继续校验。
            }

            var afterSecond = repo.GetByIdAsync(seeded.Id).GetAwaiter().GetResult();
            Assert.NotNull(afterSecond);

            // 期望（修复后）：终态任务不被非法翻转，关键字段保持不变。
            Assert.True(
                afterSecond!.Status == afterFirst!.Status,
                $"终态被非法翻转：首次 {afterFirst.Status} → 第二次请求"
                + $"{(secondIsComplete ? "Complete" : "Fail")} 后变为 {afterSecond.Status}。"
                + "应依据状态机拒绝非法转换。");

            Assert.True(
                afterSecond.ExtractedText == afterFirst.ExtractedText,
                "终态任务的 ExtractedText 被覆盖："
                + $"\"{afterFirst.ExtractedText}\" → \"{afterSecond.ExtractedText}\"。");

            Assert.True(
                afterSecond.MetadataJson == afterFirst.MetadataJson,
                "终态任务的 MetadataJson 被覆盖："
                + $"\"{afterFirst.MetadataJson}\" → \"{afterSecond.MetadataJson}\"。");
        });
    }

    /// <summary>
    /// 具体反例 1（确定性单元用例）：Completed → Failed 必须被拒绝。
    /// </summary>
    [Fact]
    public void Completed_task_cannot_be_flipped_to_failed()
    {
        var repo = new InMemoryOcrResultRepository();
        var service = NewService(repo);
        var seeded = SeedPending(repo);

        service.CompleteOcrTaskAsync(seeded.Id, "recognized text").GetAwaiter().GetResult();
        var completed = repo.GetByIdAsync(seeded.Id).GetAwaiter().GetResult();

        try
        {
            service.FailOcrTaskAsync(seeded.Id, "forced failure").GetAwaiter().GetResult();
        }
        catch (InvalidOperationException)
        {
        }

        var after = repo.GetByIdAsync(seeded.Id).GetAwaiter().GetResult();
        Assert.Equal(OcrStatus.Completed, after!.Status);
        Assert.Equal(completed!.ExtractedText, after.ExtractedText);
    }

    /// <summary>
    /// 具体反例 2（确定性单元用例）：Failed → Completed 必须被拒绝。
    /// </summary>
    [Fact]
    public void Failed_task_cannot_be_flipped_to_completed()
    {
        var repo = new InMemoryOcrResultRepository();
        var service = NewService(repo);
        var seeded = SeedPending(repo);

        service.FailOcrTaskAsync(seeded.Id, "ocr failed").GetAwaiter().GetResult();
        var failed = repo.GetByIdAsync(seeded.Id).GetAwaiter().GetResult();

        try
        {
            service.CompleteOcrTaskAsync(seeded.Id, "late text").GetAwaiter().GetResult();
        }
        catch (InvalidOperationException)
        {
        }

        var after = repo.GetByIdAsync(seeded.Id).GetAwaiter().GetResult();
        Assert.Equal(OcrStatus.Failed, after!.Status);
        Assert.Equal(failed!.ExtractedText, after.ExtractedText);
        Assert.Equal(failed.MetadataJson, after.MetadataJson);
    }

    private static LocalOcrService NewService(InMemoryOcrResultRepository repo)
        => new(
            repo,
            new RecordingActivityLogRepository(),
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

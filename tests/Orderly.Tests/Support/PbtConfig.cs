using System.Runtime.CompilerServices;
using CsCheck;

namespace Orderly.Tests.Support;

/// <summary>
/// Shared configuration for the property-based testing harness (CsCheck layered on the
/// xUnit runner). It guarantees that every property test in this assembly executes at
/// least <see cref="MinIterations"/> iterations.
/// </summary>
/// <remarks>
/// CsCheck reads its global iteration budget from the static <c>Check.Iter</c> field
/// (default 100), which can be overridden by the <c>CsCheck_Iter</c> environment variable.
/// The module initializer below raises that budget back up to the configured minimum if
/// anything (for example, an environment variable) would otherwise lower it, so no
/// property test can silently run fewer than <see cref="MinIterations"/> iterations.
/// Property tests SHOULD pass <c>iter: PbtConfig.MinIterations</c> explicitly when they
/// want the minimum applied locally regardless of the global setting.
/// </remarks>
public static class PbtConfig
{
    /// <summary>The minimum number of iterations every property test must execute.</summary>
    public const long MinIterations = 100;

    /// <summary>
    /// Runs once when the test assembly's module loads. Ensures the global CsCheck
    /// iteration budget is never below <see cref="MinIterations"/>.
    /// </summary>
    [ModuleInitializer]
    public static void EnsureMinimumIterations()
    {
        // 大量 SQLite 属性测试会在清理临时库时调用全局 ClearAllPools；CsCheck 多线程采样会
        // 释放同一进程内其他样本正在使用的连接，因此数据库属性测试必须串行采样。
        Check.Threads = 1;

        if (Check.Iter < MinIterations)
        {
            Check.Iter = MinIterations;
        }
    }
}

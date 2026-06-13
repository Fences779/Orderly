using System;
using Orderly.Core.Services;

namespace Orderly.App.Services;

/// <summary>
/// <see cref="IToastService"/> 的壳层转发器（集成接线任务 21.1 / design §5.3 / BC-7）。
///
/// <para><b>解决先后依赖</b>：壳层 Toast 由 <c>MainWindow</c> 实现（<see cref="IToastService"/>），
/// 但 <c>MainWindow</c> 在 <c>MainViewModel</c>（及其组合的 <c>Settings</c> / <c>MeProfile</c> 子 VM）
/// <b>之后</b>创建（<c>DataContext = viewModel</c>）。子 VM 在构造时即以只读字段持有 <see cref="IToastService"/>，
/// 无法在 <c>MainWindow</c> 创建后回填该只读字段。</para>
///
/// <para>本转发器在子 VM 构造时即可注入（作为稳定的 <see cref="IToastService"/> 接缝），其内部目标
/// <see cref="Target"/> 在 <c>MainWindow</c> 创建后再回填为该窗口实例。回填前的任何 <see cref="Show"/>
/// 调用安全降级为无操作（壳层尚未就绪，不抛异常），回填后透传给真实 Toast 实现。</para>
/// </summary>
public sealed class ToastServiceRelay : IToastService
{
    /// <summary>
    /// 真实的壳层 Toast 目标（通常为 <c>MainWindow</c>）。在 <c>MainWindow</c> 创建后回填；
    /// 为 <see langword="null"/> 时 <see cref="Show"/> 安全降级为无操作。
    /// </summary>
    public IToastService? Target { get; set; }

    /// <inheritdoc />
    public void Show(string message, ToastSeverity severity = ToastSeverity.Info, TimeSpan? duration = null)
        => Target?.Show(message, severity, duration);
}

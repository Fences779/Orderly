namespace Orderly.Core.Services;

/// <summary>
/// Toast 提示的严重级别（需求 10.7，design §5.3 / BC-7）。
///
/// 用于壳层通用 Toast 服务对不同结果着色：成功提示用 <see cref="Success"/>、
/// 失败提示用 <see cref="Error"/>，一般信息用 <see cref="Info"/>。
/// </summary>
public enum ToastSeverity
{
    /// <summary>一般信息提示（默认）。</summary>
    Info,

    /// <summary>成功提示（如「设置已保存」）。</summary>
    Success,

    /// <summary>警告提示。</summary>
    Warning,

    /// <summary>错误 / 失败提示（如保存失败的人话说明 + 错误码）。</summary>
    Error,
}

/// <summary>
/// 壳层通用 Toast 服务抽象（需求 10.7，design §5.3 / BC-7）。
///
/// 由 <c>MainWindow</c> 中既有的 <c>Popup_CopyToast</c> 泛化而来，供 ViewModel 经服务接缝
/// 触发轻量提示，而无需直接操作 UI 控件（保持 MVVM 纯净度 P5）。复制提示与设置保存结果
/// 提示统一经其呈现。
/// </summary>
public interface IToastService
{
    /// <summary>
    /// 显示一条自动消失的轻量提示。
    /// </summary>
    /// <param name="message">面向用户的提示文案（中文）。</param>
    /// <param name="severity">提示严重级别，决定着色；默认 <see cref="ToastSeverity.Info"/>。</param>
    /// <param name="duration">提示停留时长；为 <c>null</c> 时采用默认时长。</param>
    void Show(string message, ToastSeverity severity = ToastSeverity.Info, TimeSpan? duration = null);
}

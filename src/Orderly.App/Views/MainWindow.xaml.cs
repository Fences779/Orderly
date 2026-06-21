using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Orderly.App.ViewModels;
using Orderly.Core.Services;

namespace Orderly.App.Views;

/// <summary>
/// 壳层窗口。除导航宿主外，亦实现 <see cref="IToastService"/>（design §5.3 / BC-7，需求 10.7）：
/// 既有的 <c>Popup_CopyToast</c> 已泛化为通用 Toast（<c>Popup_Toast</c>），复制提示与设置保存结果
/// 提示统一经此呈现，ViewModel 经服务接缝触发而不直接操作控件（保持 MVVM 纯净度 P5）。
/// </summary>
public partial class MainWindow : Window, IToastService
{
    private static readonly TimeSpan DefaultToastDuration = TimeSpan.FromMilliseconds(1500);

    private CancellationTokenSource? _toastCts;
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        System.Windows.Application.Current.MainWindow = this;
    }

    /// <summary>
    /// 当主窗口关闭按钮被拦截并隐藏到托盘时触发，供壳层接入应用级会话锁定
    /// 触发点（任务 9.8，需求 18.1/18.2/13.3）。
    /// </summary>
    public event EventHandler? HiddenToTray;

    /// <summary>
    /// 当用户关闭主窗口且未启用“关闭窗口后最小化到托盘”时触发，
    /// 由应用统一完成资源清理和进程退出。
    /// </summary>
    public event EventHandler? ExitRequested;

    protected override void OnClosing(CancelEventArgs e)
    {
        if (System.Windows.Application.Current is App { IsExiting: false, IsSwitchingSession: false })
        {
            e.Cancel = true;

            if (_viewModel.StartMinimizedToTrayInput)
            {
                Hide();
                HiddenToTray?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                ExitRequested?.Invoke(this, EventArgs.Empty);
            }

            return;
        }

        base.OnClosing(e);
    }

    /// <summary>
    /// <see cref="IToastService.Show"/> 实现：在 UI 线程封送（Dispatcher）后呈现一条
    /// 自动消失的轻量提示，按 <paramref name="severity"/> 着色。
    /// </summary>
    public void Show(string message, ToastSeverity severity = ToastSeverity.Info, TimeSpan? duration = null)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => Show(message, severity, duration));
            return;
        }

        ShowToastInternal(message ?? string.Empty, severity, duration ?? DefaultToastDuration);
    }

    private void ShowToastInternal(string message, ToastSeverity severity, TimeSpan duration)
    {
        _toastCts?.Cancel();
        _toastCts = new CancellationTokenSource();
        var token = _toastCts.Token;

        ApplyToastSeverity(severity);
        Text_Toast.Text = message;
        Popup_Toast.IsOpen = true;

        Task.Delay(duration, token).ContinueWith(
            _ =>
            {
                if (!token.IsCancellationRequested)
                {
                    Dispatcher.Invoke(() => Popup_Toast.IsOpen = false);
                }
            },
            token,
            TaskContinuationOptions.OnlyOnRanToCompletion,
            TaskScheduler.Default);
    }

    private void ApplyToastSeverity(ToastSeverity severity)
    {
        // 不同结果用不同底色 + 图标着色（成功/失败区分，需求 10.7）。
        (string background, string glyph) = severity switch
        {
            ToastSeverity.Success => ("#1E8E3E", "\uE73E"), // 对勾，绿色
            ToastSeverity.Warning => ("#B7791F", "\uE7BA"), // 警告，琥珀色
            ToastSeverity.Error => ("#C5221F", "\uE783"),   // 错误，红色
            _ => ("#222224", "\uE946"),                      // 信息，深色
        };

        Border_Toast.Background = new SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(background));
        Icon_Toast.Text = glyph;
    }

    // Exposed so extracted section UserControls can surface the shared window-level
    // toast without owning the Popup. Copy notifications are surfaced as a success toast.
    internal void ShowCopyToastMessage(string message) => Show(message, ToastSeverity.Success);

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Orderly.App.Helpers.DwmHelper.UpdateTitleBarColor(this);
        Orderly.App.Helpers.ThemeHelper.ThemeChanged += OnThemeChanged;
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        Orderly.App.Helpers.DwmHelper.UpdateTitleBarColor(this);
    }

    protected override void OnClosed(EventArgs e)
    {
        Orderly.App.Helpers.ThemeHelper.ThemeChanged -= OnThemeChanged;
        base.OnClosed(e);
    }

    private void Window_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (Orderly.App.Helpers.SettingsHelper.GetIsSelectingStartupSection(this))
        {
            if (e.OriginalSource is DependencyObject dep)
            {
                System.Windows.Controls.Button? button = null;
                DependencyObject? current = dep;
                while (current != null)
                {
                    if (current is System.Windows.Controls.Button b)
                    {
                        button = b;
                        break;
                    }
                    current = VisualTreeHelper.GetParent(current);
                }

                if (button != null)
                {
                    if (button.CommandParameter is string sectionName)
                    {
                        var mainVM = DataContext as MainViewModel;
                        if (mainVM != null && mainVM.StartupSectionOptions.Contains(sectionName))
                        {
                            mainVM.StartupDefaultSectionInput = sectionName;
                            Orderly.App.Helpers.SettingsHelper.SetIsSelectingStartupSection(this, false);
                            Show($"已成功将“{sectionName}”设定为默认启动页", ToastSeverity.Success);
                            e.Handled = true;
                        }
                    }
                }
            }
        }
    }
}

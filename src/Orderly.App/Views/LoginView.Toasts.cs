using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Animation;

namespace Orderly.App.Views;

public partial class LoginView : Window
{
    private int _overlayVersion;

    private void ShowSuccessToast(string message)
    {
        ToastOverlay.ShowSuccess(message);
        _ = ShowRightPaneOverlayAsync(2000 + 180 + 240);
    }

    private void ShowErrorToast(string message)
    {
        ToastOverlay.ShowError(message);
        _ = ShowRightPaneOverlayAsync(2800 + 180 + 240);
    }

    /// <summary>
    /// Toast 显示期间在右侧叠加半透明遮罩，Toast 消失后同步隐藏遮罩。
    /// totalDuration 与 LoginToastOverlay 里的 delay + fade-out 时长保持一致。
    /// </summary>
    private async Task ShowRightPaneOverlayAsync(int totalDurationMs)
    {
        var version = ++_overlayVersion;

        RightPaneOverlay.Visibility = Visibility.Visible;

        // 淡入
        AnimateOverlay(0d, 1d, TimeSpan.FromMilliseconds(180));

        await Task.Delay(totalDurationMs - 240);
        if (version != _overlayVersion) return;

        // 淡出，结束后 Collapsed
        AnimateOverlay(1d, 0d, TimeSpan.FromMilliseconds(240), () =>
        {
            if (version != _overlayVersion) return;
            RightPaneOverlay.Visibility = Visibility.Collapsed;
        });
    }

    private void AnimateOverlay(double from, double to, TimeSpan duration, Action? completed = null)
    {
        var anim = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = new Duration(duration),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        if (completed is not null)
            anim.Completed += (_, _) => completed();

        RightPaneOverlay.BeginAnimation(OpacityProperty, anim);
    }
}

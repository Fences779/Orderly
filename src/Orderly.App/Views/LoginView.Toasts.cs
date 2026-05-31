using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Orderly.Core.Security;

namespace Orderly.App.Views;

public partial class LoginView : Window
{
    private async void ShowSuccessToast(string message)
    {
        var toastVersion = ++_successToastVersion;
        TxtSuccessToast.Text = message;
        SuccessToastOverlay.Visibility = Visibility.Visible;
        SuccessToastOverlay.Opacity = 0d;
        SuccessToastTranslateTransform.Y = 10d;

        AnimateDouble(
            SuccessToastOverlay,
            UIElement.OpacityProperty,
            1d,
            TimeSpan.FromMilliseconds(180));
        AnimateDouble(
            SuccessToastTranslateTransform,
            TranslateTransform.YProperty,
            0d,
            TimeSpan.FromMilliseconds(180));

        await Task.Delay(2000);
        if (toastVersion != _successToastVersion)
        {
            return;
        }

        AnimateDouble(
            SuccessToastOverlay,
            UIElement.OpacityProperty,
            0d,
            TimeSpan.FromMilliseconds(240),
            () =>
            {
                if (toastVersion != _successToastVersion)
                {
                    return;
                }

                SuccessToastOverlay.Visibility = Visibility.Collapsed;
            });
        AnimateDouble(
            SuccessToastTranslateTransform,
            TranslateTransform.YProperty,
            -6d,
            TimeSpan.FromMilliseconds(240));
    }

    private async void ShowErrorToast(string message)
    {
        var toastVersion = ++_errorToastVersion;
        TxtErrorToast.Text = FormatErrorToastMessage(message);
        ErrorToastOverlay.Visibility = Visibility.Visible;
        ErrorToastOverlay.Opacity = 0d;
        ErrorToastTranslateTransform.Y = 10d;

        AnimateDouble(
            ErrorToastOverlay,
            UIElement.OpacityProperty,
            1d,
            TimeSpan.FromMilliseconds(180));
        AnimateDouble(
            ErrorToastTranslateTransform,
            TranslateTransform.YProperty,
            0d,
            TimeSpan.FromMilliseconds(180));

        await Task.Delay(2800);
        if (toastVersion != _errorToastVersion)
        {
            return;
        }

        AnimateDouble(
            ErrorToastOverlay,
            UIElement.OpacityProperty,
            0d,
            TimeSpan.FromMilliseconds(240),
            () =>
            {
                if (toastVersion != _errorToastVersion)
                {
                    return;
                }

                ErrorToastOverlay.Visibility = Visibility.Collapsed;
            });
        AnimateDouble(
            ErrorToastTranslateTransform,
            TranslateTransform.YProperty,
            -6d,
            TimeSpan.FromMilliseconds(240));
    }

    private static string FormatErrorToastMessage(string message)
    {
        if (string.Equals(message, MasterPasswordPolicy.ValidationMessage, StringComparison.Ordinal))
        {
            return "新密码不符合要求\n至少 8 位，且必须同时包含大小写字母和数字\n不能包含空白字符";
        }

        return message;
    }

    private static void AnimateDouble(
        DependencyObject target,
        DependencyProperty property,
        double to,
        TimeSpan duration,
        Action? completed = null)
    {
        var animation = new DoubleAnimation
        {
            To = to,
            Duration = new Duration(duration),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        if (completed is not null)
        {
            animation.Completed += (_, _) => completed();
        }

        switch (target)
        {
            case Animatable animatable:
                animatable.BeginAnimation(property, animation);
                break;
            case UIElement element:
                element.BeginAnimation(property, animation);
                break;
        }
    }
}

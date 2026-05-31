using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Orderly.App.Views;

public partial class LoginView : Window
{
    private LoginSurface ResolveCurrentSurface()
    {
        if (_viewModel.IsFirstRunMode)
        {
            return LoginSurface.OwnerCreate;
        }

        if (_viewModel.IsPasswordRecoveryMode)
        {
            return LoginSurface.PasswordRecovery;
        }

        if (_viewModel.IsAccountManagementMode)
        {
            return LoginSurface.AccountManagement;
        }

        if (_viewModel.IsCreateManagedAccountMode)
        {
            return LoginSurface.CreateMember;
        }

        return LoginSurface.SignIn;
    }

    private void ApplySurfaceStateImmediately(LoginSurface surface)
    {
        _currentSurface = surface;
        ApplyPanelState(SignInPanel, surface == LoginSurface.SignIn);
        ApplyPanelState(PasswordRecoveryPanel, surface == LoginSurface.PasswordRecovery);
        ApplyPanelState(AccountManagementPanel, surface == LoginSurface.AccountManagement);
        ApplyPanelState(CreateManagedAccountPanel, surface == LoginSurface.CreateMember);
        OwnerCreatePanel.Visibility = surface == LoginSurface.OwnerCreate ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task TransitionToSurfaceAsync(LoginSurface targetSurface)
    {
        var transitionVersion = ++_transitionVersion;
        var previousSurface = _currentSurface;

        if (targetSurface == LoginSurface.OwnerCreate || previousSurface == LoginSurface.OwnerCreate)
        {
            ApplySurfaceStateImmediately(targetSurface);
            return;
        }

        var outgoingPanel = GetPanel(previousSurface);
        var incomingPanel = GetPanel(targetSurface);

        if (incomingPanel is null)
        {
            ApplySurfaceStateImmediately(targetSurface);
            return;
        }

        _currentSurface = targetSurface;
        OwnerCreatePanel.Visibility = Visibility.Collapsed;
        PrepareIncomingPanel(incomingPanel);

        var outgoingTask = outgoingPanel is null
            ? Task.CompletedTask
            : AnimatePanelAsync(outgoingPanel, 0d);
        var incomingTask = AnimatePanelAsync(incomingPanel, 1d);

        await Task.WhenAll(outgoingTask, incomingTask);

        if (transitionVersion != _transitionVersion)
        {
            return;
        }

        if (outgoingPanel is not null)
        {
            ApplyPanelState(outgoingPanel, false);
        }

        ApplyPanelState(incomingPanel, true);
    }

    private async Task WaitForSurfaceAsync(LoginSurface surface)
    {
        for (var i = 0; i < 20; i++)
        {
            if (_currentSurface == surface)
            {
                return;
            }

            await Task.Delay(15);
        }
    }

    private static void PrepareIncomingPanel(FrameworkElement panel)
    {
        panel.Visibility = Visibility.Visible;
        panel.Opacity = 0d;
        EnsureTranslateTransform(panel).X = 0d;
    }

    private static void ApplyPanelState(FrameworkElement panel, bool isVisible)
    {
        panel.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        panel.Opacity = isVisible ? 1d : 0d;
        EnsureTranslateTransform(panel).X = 0d;
    }

    private static TranslateTransform EnsureTranslateTransform(UIElement element)
    {
        if (element.RenderTransform is not TranslateTransform transform)
        {
            transform = new TranslateTransform();
            element.RenderTransform = transform;
        }

        return transform;
    }

    private static Task AnimatePanelAsync(FrameworkElement panel, double targetOpacity)
    {
        var storyboard = new Storyboard();
        var completion = new TaskCompletionSource<object?>();

        var opacityAnimation = new DoubleAnimation
        {
            To = targetOpacity,
            Duration = new Duration(SurfaceTransitionDuration),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        Storyboard.SetTarget(opacityAnimation, panel);
        Storyboard.SetTargetProperty(opacityAnimation, new PropertyPath(UIElement.OpacityProperty));

        storyboard.Children.Add(opacityAnimation);
        storyboard.Completed += (_, _) => completion.TrySetResult(null);
        storyboard.Begin();

        return completion.Task;
    }

    private FrameworkElement? GetPanel(LoginSurface surface)
    {
        return surface switch
        {
            LoginSurface.SignIn => SignInPanel,
            LoginSurface.PasswordRecovery => PasswordRecoveryPanel,
            LoginSurface.AccountManagement => AccountManagementPanel,
            LoginSurface.CreateMember => CreateManagedAccountPanel,
            _ => null
        };
    }

    private enum LoginSurface
    {
        SignIn,
        PasswordRecovery,
        AccountManagement,
        CreateMember,
        OwnerCreate
    }
}

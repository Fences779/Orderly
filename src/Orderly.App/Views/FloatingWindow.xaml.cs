using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Orderly.Core.Models;
using Orderly.Core.Repositories;
using Orderly.App.ViewModels;

namespace Orderly.App.Views;

public partial class FloatingWindow : Window
{
    private readonly IAppSettingRepository _settingRepository;
    private readonly Action _openMainWindow;
    private readonly Action<string> _navigateToSection;
    private readonly Action _exitApplication;
    private System.Windows.Point _dragStart;
    private System.Windows.Point _windowStart;
    private bool _isDragging;
    private bool _isInitialized;
    private bool _isApplyingOpacity = true;
    private double _restingOpacity = 0.82;

    public FloatingWindow(
        FloatingWindowViewModel viewModel,
        AppPreferences preferences,
        IAppSettingRepository settingRepository,
        Action openMainWindow,
        Action<string> navigateToSection,
        Action exitApplication)
    {
        _settingRepository = settingRepository;
        _openMainWindow = openMainWindow;
        _navigateToSection = navigateToSection;
        _exitApplication = exitApplication;

        InitializeComponent();
        DataContext = viewModel;
        ApplyInitialState(preferences);
        _isInitialized = true;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (System.Windows.Application.Current is App { IsExiting: false })
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnClosing(e);
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }
    }

    private void ApplyInitialState(AppPreferences preferences)
    {
        ApplyRuntimePreferences(preferences);

        if (IsUsablePoint(preferences.FloatingBallLeft, preferences.FloatingBallTop))
        {
            Left = preferences.FloatingBallLeft;
            Top = preferences.FloatingBallTop;
            return;
        }

        Left = SystemParameters.WorkArea.Right - Width - 28;
        Top = SystemParameters.WorkArea.Top + 96;
    }

    public void ApplyRuntimePreferences(AppPreferences preferences)
    {
        _restingOpacity = Math.Clamp(preferences.FloatingBallOpacity, 0.35, 1.0);

        _isApplyingOpacity = true;
        Slider_Opacity.Value = _restingOpacity;
        _isApplyingOpacity = false;

        Opacity = Root.IsMouseOver || Root.ContextMenu?.IsOpen == true
            ? 1.0
            : _restingOpacity;
    }

    private static bool IsUsablePoint(double left, double top)
    {
        if (double.IsNaN(left) || double.IsNaN(top) || double.IsInfinity(left) || double.IsInfinity(top))
        {
            return false;
        }

        var area = SystemParameters.WorkArea;
        return left >= area.Left - 24
            && top >= area.Top - 24
            && left <= area.Right - 24
            && top <= area.Bottom - 24;
    }

    private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = GetScreenPosition(e);
        _windowStart = new System.Windows.Point(Left, Top);
        _isDragging = false;

        if (e.ClickCount == 2)
        {
            _openMainWindow();
            e.Handled = true;
            return;
        }

        Root.CaptureMouse();
        e.Handled = true;
    }

    private void Root_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || !Root.IsMouseCaptured)
        {
            return;
        }

        var current = GetScreenPosition(e);
        if (!_isDragging
            && Math.Abs(current.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(current.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _isDragging = true;
        Left = _windowStart.X + current.X - _dragStart.X;
        Top = _windowStart.Y + current.Y - _dragStart.Y;
        e.Handled = true;
    }

    private void Root_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (Root.IsMouseCaptured)
        {
            Root.ReleaseMouseCapture();
        }

        if (_isDragging)
        {
            _ = SavePositionAsync();
            _isDragging = false;
            e.Handled = true;
            return;
        }

        _openMainWindow();
        e.Handled = true;
    }

    private System.Windows.Point GetScreenPosition(System.Windows.Input.MouseEventArgs e)
    {
        var devicePoint = PointToScreen(e.GetPosition(this));
        var source = PresentationSource.FromVisual(this);
        return source?.CompositionTarget?.TransformFromDevice.Transform(devicePoint) ?? devicePoint;
    }

    private void Root_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        OpenContextMenu();
        e.Handled = true;
    }

    private void OpenContextMenu()
    {
        if (Root.ContextMenu is null)
        {
            return;
        }

        Root.ContextMenu.PlacementTarget = Root;
        Root.ContextMenu.IsOpen = true;
    }

    private void Root_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        Opacity = 1.0;
    }

    private void Root_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (Root.ContextMenu?.IsOpen == true)
        {
            return;
        }

        Opacity = _restingOpacity;
    }

    private void MenuNavigate_Click(object sender, RoutedEventArgs e)
    {
        if (Root.ContextMenu is not null)
        {
            Root.ContextMenu.IsOpen = false;
        }

        if (sender is MenuItem { Tag: string section })
        {
            _navigateToSection(section);
        }
    }

    private void MenuHide_Click(object sender, RoutedEventArgs e)
    {
        if (Root.ContextMenu is not null)
        {
            Root.ContextMenu.IsOpen = false;
        }

        Hide();
    }

    private void MenuExit_Click(object sender, RoutedEventArgs e)
    {
        if (Root.ContextMenu is not null)
        {
            Root.ContextMenu.IsOpen = false;
        }

        _exitApplication();
    }

    private void Slider_Opacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_isInitialized || _isApplyingOpacity)
        {
            return;
        }

        _restingOpacity = Math.Clamp(e.NewValue, 0.35, 1.0);
        Opacity = _restingOpacity;
        _ = _settingRepository.UpsertAsync(AppSettingKeys.FloatingBallOpacity, _restingOpacity.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
    }

    private async Task SavePositionAsync()
    {
        try
        {
            await _settingRepository.UpsertAsync(AppSettingKeys.FloatingBallLeft, Left.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            await _settingRepository.UpsertAsync(AppSettingKeys.FloatingBallTop, Top.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        }
        catch
        {
        }
    }
}

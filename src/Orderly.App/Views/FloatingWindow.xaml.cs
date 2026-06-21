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
    private System.Windows.Point _dragStart;
    private bool _isDragging;
    private bool _isApplyingOpacity;
    private double _restingOpacity = 0.82;

    public FloatingWindow(
        FloatingWindowViewModel viewModel,
        AppPreferences preferences,
        IAppSettingRepository settingRepository,
        Action openMainWindow,
        Action<string> navigateToSection)
    {
        InitializeComponent();
        DataContext = viewModel;
        _settingRepository = settingRepository;
        _openMainWindow = openMainWindow;
        _navigateToSection = navigateToSection;

        ApplyInitialState(preferences);
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

    private void ApplyInitialState(AppPreferences preferences)
    {
        _restingOpacity = Math.Clamp(preferences.FloatingBallOpacity, 0.35, 1.0);
        Opacity = _restingOpacity;

        _isApplyingOpacity = true;
        Slider_Opacity.Value = _restingOpacity;
        _isApplyingOpacity = false;

        if (IsUsablePoint(preferences.FloatingBallLeft, preferences.FloatingBallTop))
        {
            Left = preferences.FloatingBallLeft;
            Top = preferences.FloatingBallTop;
            return;
        }

        Left = SystemParameters.WorkArea.Right - Width - 28;
        Top = SystemParameters.WorkArea.Top + 96;
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
        _dragStart = e.GetPosition(this);
        _isDragging = false;

        if (e.ClickCount == 2)
        {
            _openMainWindow();
            e.Handled = true;
            return;
        }

        CaptureMouse();
    }

    private void Root_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || !IsMouseCaptured)
        {
            return;
        }

        var current = e.GetPosition(this);
        if (!_isDragging
            && Math.Abs(current.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(current.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _isDragging = true;
        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void Root_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }

        if (_isDragging)
        {
            _ = SavePositionAsync();
            _isDragging = false;
            return;
        }

        Root.ContextMenu ??= (ContextMenu)FindResource("FloatingBallMenu");
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
        if (sender is MenuItem { Tag: string section })
        {
            _navigateToSection(section);
        }
    }

    private void MenuHide_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    private void Slider_Opacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isApplyingOpacity)
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

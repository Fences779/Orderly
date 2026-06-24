using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace Orderly.App.Views;

public partial class PinUnlockView : Window
{
    public PinUnlockView(string displayName, string username, bool isWindowsHelloAvailable = false)
    {
        InitializeComponent();
        TxtAccount.Text = $"账号：{displayName}（{username}）";
        BtnWindowsHello.Visibility = isWindowsHelloAvailable ? Visibility.Visible : Visibility.Collapsed;
        Loaded += (_, _) => TxtPin.Focus();
    }

    public string EnteredPin { get; private set; } = string.Empty;
    public PinUnlockMethod UnlockMethod { get; private set; } = PinUnlockMethod.Pin;

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void BtnUnlock_Click(object sender, RoutedEventArgs e)
    {
        SubmitUnlock();
    }

    private void BtnLogout_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void BtnWindowsHello_Click(object sender, RoutedEventArgs e)
    {
        UnlockMethod = PinUnlockMethod.WindowsHello;
        DialogResult = true;
        Close();
    }

    private void TxtPin_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            SubmitUnlock();
        }
    }

    private void TxtPin_PasswordChanged(object sender, RoutedEventArgs e)
    {
        FadePlaceholder(string.IsNullOrEmpty(TxtPin.Password));
    }

    private void FadePlaceholder(bool show)
    {
        double targetOpacity = show ? 0.5 : 0.0;
        if (TxtPlaceholder.Opacity == targetOpacity) return;

        var animation = new System.Windows.Media.Animation.DoubleAnimation
        {
            To = targetOpacity,
            Duration = TimeSpan.FromSeconds(0.15),
            EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut }
        };
        TxtPlaceholder.BeginAnimation(UIElement.OpacityProperty, animation);
    }

    private void SubmitUnlock()
    {
        var pin = TxtPin.Password.Trim();
        if (pin.Length != 6 || !pin.All(char.IsDigit))
        {
            TxtError.Text = "PIN 必须为 6 位数字。";
            TxtError.Visibility = Visibility.Visible;
            return;
        }

        EnteredPin = pin;
        UnlockMethod = PinUnlockMethod.Pin;
        DialogResult = true;
        Close();
    }
}

public enum PinUnlockMethod
{
    Pin,
    WindowsHello
}

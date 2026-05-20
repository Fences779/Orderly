using System.Windows;
using System.Windows.Input;

namespace Orderly.App.Views;

public partial class PinUnlockView : Window
{
    public PinUnlockView(string displayName, string username)
    {
        InitializeComponent();
        TxtAccount.Text = $"账号：{displayName}（{username}）";
        Loaded += (_, _) => TxtPin.Focus();
    }

    public string EnteredPin { get; private set; } = string.Empty;

    private void BtnUnlock_Click(object sender, RoutedEventArgs e)
    {
        SubmitUnlock();
    }

    private void BtnLogout_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void TxtPin_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            SubmitUnlock();
        }
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
        DialogResult = true;
        Close();
    }
}

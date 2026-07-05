using System.Windows;
using System.Windows.Media;

namespace Orderly.App.Views;

public enum MessageDialogType
{
    Success,
    Info,
    Warning,
    Error
}

public partial class MessageDialog : Window
{
    public MessageDialog(string title, string message, MessageDialogType type = MessageDialogType.Info)
    {
        InitializeComponent();
        
        TitleText.Text = title;
        MessageText.Text = message;
        
        ApplyTypeStyle(type);
    }

    private void ApplyTypeStyle(MessageDialogType type)
    {
        switch (type)
        {
            case MessageDialogType.Success:
                IconBorder.Background = (System.Windows.Media.Brush)FindResource("AccentSoftBrush");
                IconText.Foreground = (System.Windows.Media.Brush)FindResource("AccentTextBrush");
                IconText.Text = "\xE73E"; // Checkmark
                break;
            case MessageDialogType.Info:
                IconBorder.Background = (System.Windows.Media.Brush)FindResource("BlueSoftBrush");
                IconText.Foreground = (System.Windows.Media.Brush)FindResource("BlueTextBrush");
                IconText.Text = "\xE946"; // Info
                break;
            case MessageDialogType.Warning:
                IconBorder.Background = (System.Windows.Media.Brush)FindResource("WarmSoftBrush");
                IconText.Foreground = (System.Windows.Media.Brush)FindResource("WarmTextBrush");
                IconText.Text = "\xE7BA"; // Warning
                break;
            case MessageDialogType.Error:
                IconBorder.Background = (System.Windows.Media.Brush)FindResource("DangerSoftBrush");
                IconText.Foreground = (System.Windows.Media.Brush)FindResource("DangerTextBrush");
                IconText.Text = "\xE10A"; // Error / Cancel
                break;
        }
    }

    private bool _isClosing = false;
    private bool? _pendingDialogResult = null;

    private void CloseWithAnimation(bool? dialogResult)
    {
        if (_isClosing) return;
        _isClosing = true;
        _pendingDialogResult = dialogResult;

        var sb = (System.Windows.Media.Animation.Storyboard)FindResource("CloseStoryboard");
        sb.Completed += (s, e) =>
        {
            try
            {
                DialogResult = _pendingDialogResult;
            }
            catch
            {
                // 以防它不是模态窗口弹出
            }
            Close();
        };
        sb.Begin();
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        CloseWithAnimation(true);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        CloseWithAnimation(false);
    }

    private void Border_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
        {
            DragMove();
        }
    }
}

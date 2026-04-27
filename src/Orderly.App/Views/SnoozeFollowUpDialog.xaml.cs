using System.Globalization;
using System.Windows;

namespace Orderly.App.Views;

public partial class SnoozeFollowUpDialog : Window
{
    public SnoozeFollowUpDialog(DateTime currentScheduledAt)
    {
        InitializeComponent();
        ScheduledAtTextBox.Text = currentScheduledAt.AddDays(1).ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);
        Loaded += (_, _) => ScheduledAtTextBox.Focus();
    }

    public DateTime ScheduledAt { get; private set; }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        if (!DateTime.TryParse(ScheduledAtTextBox.Text.Trim(), CultureInfo.CurrentCulture, DateTimeStyles.None, out var scheduledAt))
        {
            System.Windows.MessageBox.Show(this, "跟进时间格式不正确。", "延期跟进", MessageBoxButton.OK, MessageBoxImage.Warning);
            ScheduledAtTextBox.Focus();
            return;
        }

        ScheduledAt = scheduledAt;
        DialogResult = true;
    }
}

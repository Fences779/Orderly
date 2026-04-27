using System.Globalization;
using System.Windows;

namespace Orderly.App.Views;

public partial class AddFollowUpDialog : Window
{
    public AddFollowUpDialog()
    {
        InitializeComponent();
        ScheduledAtTextBox.Text = DateTime.Now.AddDays(1).ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);
        Loaded += (_, _) => TitleTextBox.Focus();
    }

    public string FollowUpTitle { get; private set; } = string.Empty;
    public string FollowUpContent { get; private set; } = string.Empty;
    public DateTime ScheduledAt { get; private set; } = DateTime.Now.AddDays(1);

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        var title = TitleTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            System.Windows.MessageBox.Show(this, "跟进标题不能为空。", "新增跟进", MessageBoxButton.OK, MessageBoxImage.Warning);
            TitleTextBox.Focus();
            return;
        }

        if (!DateTime.TryParse(ScheduledAtTextBox.Text.Trim(), CultureInfo.CurrentCulture, DateTimeStyles.None, out var scheduledAt))
        {
            System.Windows.MessageBox.Show(this, "计划跟进时间格式不正确。", "新增跟进", MessageBoxButton.OK, MessageBoxImage.Warning);
            ScheduledAtTextBox.Focus();
            return;
        }

        FollowUpTitle = title;
        FollowUpContent = ContentTextBox.Text.Trim();
        ScheduledAt = scheduledAt;
        DialogResult = true;
    }
}

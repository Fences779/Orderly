using System.Windows;

namespace Orderly.App.Views;

public enum ConflictDialogResult
{
    Cancel,
    Refresh,
    ViewLatest
}

public partial class ConflictDialog : Window
{
    public ConflictDialog(string message, string? actorDisplayName, DateTime? updatedAt, long? latestRevision)
    {
        InitializeComponent();
        DetailText.Text = message;

        var details = new List<string>();
        if (!string.IsNullOrWhiteSpace(actorDisplayName))
            details.Add($"修改人：{actorDisplayName}");
        if (updatedAt.HasValue)
            details.Add($"修改时间：{updatedAt.Value:yyyy-MM-dd HH:mm:ss}");
        if (latestRevision.HasValue)
            details.Add($"最新版本：{latestRevision.Value}");

        if (details.Count > 0)
        {
            HintText.Text = string.Join("\n", details) + "\n\n你的修改没有覆盖对方内容。请刷新后重新确认。";
        }
    }

    public ConflictDialogResult ConflictResult { get; private set; } = ConflictDialogResult.Cancel;

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        ConflictResult = ConflictDialogResult.Refresh;
        DialogResult = true;
        Close();
    }

    private void ViewLatestButton_Click(object sender, RoutedEventArgs e)
    {
        ConflictResult = ConflictDialogResult.ViewLatest;
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        ConflictResult = ConflictDialogResult.Cancel;
        DialogResult = false;
        Close();
    }
}

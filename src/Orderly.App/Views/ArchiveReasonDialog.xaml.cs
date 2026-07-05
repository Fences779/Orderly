using System.Windows;

namespace Orderly.App.Views;

public partial class ArchiveReasonDialog : Window
{
    public ArchiveReasonDialog(string entityDescription)
    {
        InitializeComponent();
        DescriptionText.Text = $"即将归档：{entityDescription}\n归档后数据将从默认列表中隐藏，管理员可在归档数据中恢复。";
    }

    public string? Reason { get; private set; }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        Reason = ReasonTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(Reason))
        {
            System.Windows.MessageBox.Show(this, "请填写归档原因。", "归档", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

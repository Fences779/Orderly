using System.Windows;

namespace Orderly.App.Views;

public partial class ArchiveDataDialog : Window
{
    public ArchiveDataDialog()
    {
        InitializeComponent();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}

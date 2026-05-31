using System.Windows;

namespace Orderly.App.Views.Sections;

public partial class ExceptionDetailOrderInfo : System.Windows.Controls.UserControl
{
    public ExceptionDetailOrderInfo()
    {
        InitializeComponent();
    }

    private void CopyText_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is string text && !string.IsNullOrEmpty(text))
        {
            try
            {
                System.Windows.Clipboard.SetText(text);
                SectionVisualHelpers.ShowCopyToast(this, "已复制");
            }
            catch (System.Exception)
            {
                // ignore
            }
        }
    }
}

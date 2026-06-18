namespace Orderly.App.Views.Sections;

public partial class SettingsView : System.Windows.Controls.UserControl
{
    public SettingsView()
    {
        InitializeComponent();

        var maskBorder = FindName("MaskBorder") as System.Windows.Controls.Border;
        if (maskBorder != null)
        {
            maskBorder.MouseLeftButtonDown += (s, e) =>
            {
                var window = System.Windows.Window.GetWindow(this);
                if (window != null)
                {
                    Orderly.App.Helpers.SettingsHelper.SetIsSelectingStartupSection(window, false);
                }
            };
        }
    }
}

namespace Orderly.App.Views.Sections;

public partial class SettingsView : System.Windows.Controls.UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private void OnMaskClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var window = System.Windows.Window.GetWindow(this);
        if (window != null)
        {
            Orderly.App.Helpers.SettingsHelper.SetIsSelectingStartupSection(window, false);
        }
    }
}

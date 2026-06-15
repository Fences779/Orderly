namespace Orderly.App.Views.Sections;

public partial class SettingsTabAppearance : System.Windows.Controls.UserControl
{
    public SettingsTabAppearance()
    {
        InitializeComponent();
    }

    private void Btn_ChooseStartupDefaultSection_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var window = System.Windows.Window.GetWindow(this);
        if (window != null)
        {
            Orderly.App.Helpers.SettingsHelper.SetIsSelectingStartupSection(window, true);
        }
    }
}

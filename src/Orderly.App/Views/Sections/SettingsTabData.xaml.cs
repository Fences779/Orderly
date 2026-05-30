using System.Linq;
using System.Windows;
using Orderly.App.ViewModels;

namespace Orderly.App.Views.Sections;

public partial class SettingsTabData : System.Windows.Controls.UserControl
{
    public SettingsTabData()
    {
        InitializeComponent();
    }

    private void SettingsTextInput_LostFocus(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || sender is not System.Windows.Controls.TextBox textBox)
        {
            return;
        }

        var binding = textBox.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty);
        binding?.UpdateSource();

        if (System.Windows.Controls.Validation.GetHasError(textBox))
        {
            var validationError = System.Windows.Controls.Validation.GetErrors(textBox).FirstOrDefault()?.ErrorContent?.ToString();
            vm.ReportDeferredSettingsAutoSaveValidationError(
                string.IsNullOrWhiteSpace(validationError)
                    ? "当前输入无效，未自动保存。"
                    : $"当前输入无效，未自动保存：{validationError}");
            return;
        }

        vm.CommitDeferredSettingsAutoSave();
    }
}

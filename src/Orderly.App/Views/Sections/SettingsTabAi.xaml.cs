using System.Linq;
using System.Windows;
using Orderly.App.ViewModels;

namespace Orderly.App.Views.Sections;

public partial class SettingsTabAi : System.Windows.Controls.UserControl
{
    public SettingsTabAi()
    {
        InitializeComponent();
    }

    private void SettingsTextInput_LostFocus(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        System.Windows.Data.BindingExpression? binding = null;
        bool hasError = false;
        string? validationError = null;

        if (sender is System.Windows.Controls.TextBox textBox)
        {
            binding = textBox.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty);
            binding?.UpdateSource();
            hasError = System.Windows.Controls.Validation.GetHasError(textBox);
            if (hasError)
            {
                validationError = System.Windows.Controls.Validation.GetErrors(textBox).FirstOrDefault()?.ErrorContent?.ToString();
            }
        }
        else if (sender is System.Windows.Controls.ComboBox comboBox)
        {
            binding = comboBox.GetBindingExpression(System.Windows.Controls.ComboBox.SelectedValueProperty);
            binding?.UpdateSource();
            hasError = System.Windows.Controls.Validation.GetHasError(comboBox);
            if (hasError)
            {
                validationError = System.Windows.Controls.Validation.GetErrors(comboBox).FirstOrDefault()?.ErrorContent?.ToString();
            }
        }
        else
        {
            return;
        }

        if (hasError)
        {
            vm.ReportDeferredSettingsAutoSaveValidationError(
                string.IsNullOrWhiteSpace(validationError)
                    ? "当前输入无效，未自动保存。"
                    : $"当前输入无效，未自动保存：{validationError}");
            return;
        }

        vm.CommitDeferredSettingsAutoSave();
    }

}

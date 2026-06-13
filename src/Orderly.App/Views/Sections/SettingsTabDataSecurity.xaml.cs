using System.Linq;
using System.Windows;
using Orderly.App.ViewModels;

namespace Orderly.App.Views.Sections;

public partial class SettingsTabDataSecurity : System.Windows.Controls.UserControl
{
    public SettingsTabDataSecurity()
    {
        InitializeComponent();
    }

    // 任务 17.3：随「系统日志记录」卡片自 SettingsTabDataAudit 迁入，沿用既有延迟自动保存失焦提交逻辑。
    // 绑定路径相对 SettingsViewModel 的整体 DataContext 切换由任务 21.1 统一接线，故此处仍按 MainViewModel 取值。
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

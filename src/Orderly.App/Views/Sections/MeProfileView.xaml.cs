using System.Windows;
using Orderly.App.ViewModels;

namespace Orderly.App.Views.Sections;

public partial class MeProfileView : System.Windows.Controls.UserControl
{
    private MeProfileViewModel? _viewModel;

    public MeProfileView()
    {
        InitializeComponent();
        DataContextChanged += MeProfileView_DataContextChanged;
        Loaded += MeProfileView_Loaded;
        Unloaded += MeProfileView_Unloaded;
    }

    private void MeProfileView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        // 视图根 DataContext 为壳层 MainViewModel；我的页状态绑定到其组合的 MeProfile（design §8.1）。
        // 凭证 / 成员等 PasswordBox 现统一经 PasswordBoxBinder 附加行为双向绑定（任务 18.3 / Req 10.3 / 10.4），
        // code-behind 不再做密码同步或置空清空，仅保留删除成员二次确认委托的接线。
        _viewModel = (e.NewValue as MainViewModel)?.MeProfile;
        if (_viewModel is not null)
        {
            // 删除成员二次确认（任务 18.2 / Req 7.4）：接线弹窗委托，返回用户确认结果。
            // 文案明确「仅移除登录账号、保留名下历史业务数据与来源/创建人归属标签」。
            _viewModel.ConfirmDeleteMember ??= ConfirmDeleteMemberDialog;

            // Owner 紧急启用 PIN 采集（任务 18.6 / Req 17.1）：接线「独立的紧急入口弹窗」委托。
            // 弹窗与登录页完全独立（登录页保持不变）；返回 6 位明文 PIN 或 null（取消 / 非法输入）。
            _viewModel.PickEmergencyPin ??= PickEmergencyPinDialog;
        }
    }

    /// <summary>
    /// 弹出「独立的紧急入口弹窗」（任务 18.6 / Req 17.1）采集 6 位 PIN，返回明文 PIN 或 <c>null</c>。
    /// 与登录页完全独立，登录页保持不变；明文 PIN 即用即清，不缓存、不写日志（P4）。
    /// </summary>
    private string? PickEmergencyPinDialog()
    {
        var dialog = new EmergencyPinDialog
        {
            Owner = System.Windows.Window.GetWindow(this),
        };

        var confirmed = dialog.ShowDialog() == true;
        return confirmed ? dialog.EnteredPin : null;
    }

    /// <summary>
    /// 删除成员二次确认对话框（任务 18.2 / Req 7.4）：以 <see cref="MessageBox"/> 展示
    /// <see cref="MeProfileViewModel.DeleteMemberConfirmationMessage"/>，返回用户是否确认删除。
    /// </summary>
    private static bool ConfirmDeleteMemberDialog(string message)
    {
        var result = System.Windows.MessageBox.Show(
            message,
            "删除成员账号确认",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        return result == MessageBoxResult.Yes;
    }

    /// <summary>
    /// 视图加载时触发账户安全 / 登录记录卡的初始加载（任务 18.5 / Req 9.1~9.7）。
    /// VM 内部对服务未注入 / 空结果 / 读取失败均有降级处理；完整 DI 接线见任务 21.1。
    /// </summary>
    private void MeProfileView_Loaded(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null)
        {
            _viewModel = (DataContext as MainViewModel)?.MeProfile;
        }

        if (_viewModel is not null)
        {
            // 触发即弃：加载在 VM 内异步执行，异常已在 VM 内部按 Req 9.4 降级吞掉，不冒泡到 UI 线程。
            _ = _viewModel.LoadSecurityAuditAsync();
        }
    }

    private void MeProfileView_Unloaded(object sender, RoutedEventArgs e)
    {
        _viewModel = null;
    }
}

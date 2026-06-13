using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace Orderly.App.Views;

/// <summary>
/// 独立的「紧急入口弹窗」（任务 18.6，design §9.7，BC-13，Req 17.1）：仅采集 6 位 PIN。
///
/// <para>此弹窗与登录页完全独立，登录页保持不变（Req 17.x）。采集到的明文 PIN 经
/// <see cref="EnteredPin"/> 回传给调用方（<c>MeProfileViewModel.PickEmergencyPin</c> 委托），
/// 由 ViewModel 调 <c>TryEmergencyEnable</c> 校验；取消或非法输入时不回传（返回 <c>null</c>）。</para>
///
/// <para>安全约束（P4）：明文 PIN 即用即清——仅在确认时回传一次，不缓存、不写日志；
/// 窗口关闭后随之释放。</para>
/// </summary>
public partial class EmergencyPinDialog : Window
{
    public EmergencyPinDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => TxtPin.Focus();
    }

    /// <summary>确认采集到的 6 位 PIN；未确认 / 取消时为 <c>null</c>。</summary>
    public string? EnteredPin { get; private set; }

    private void BtnConfirm_Click(object sender, RoutedEventArgs e) => Submit();

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void TxtPin_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Submit();
        }
    }

    private void Submit()
    {
        var pin = TxtPin.Password.Trim();
        if (pin.Length != 6 || !pin.All(char.IsDigit))
        {
            TxtError.Text = "PIN 必须为 6 位数字。";
            TxtError.Visibility = Visibility.Visible;
            return;
        }

        EnteredPin = pin;
        DialogResult = true;
        Close();
    }
}

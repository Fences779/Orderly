using System.Windows;
using System.Windows.Controls;

namespace Orderly.App.Helpers;

/// <summary>
/// MVVM 附加行为：把 <see cref="PasswordBox.Password"/> 安全地桥接到 ViewModel 的字符串属性，
/// 取代手动的 code-behind 双向同步。当 ViewModel 绑定值被置空（命令成功后重置）时清空控件内容。
/// </summary>
/// <remarks>
/// 用法：<c>local:PasswordBoxBinder.BoundPassword="{Binding NewMemberPassword, Mode=TwoWay}"</c>
/// <para>
/// 安全约束（P4）：不为明文 Password 做长期缓存；仅在变更时同步；不引入任何把明文写入日志/诊断的路径。
/// </para>
/// </remarks>
public static class PasswordBoxBinder
{
    // 防重入标记：双向同步期间避免控件→VM→控件的循环回写。
    private static readonly DependencyProperty IsUpdatingProperty =
        DependencyProperty.RegisterAttached(
            "IsUpdating",
            typeof(bool),
            typeof(PasswordBoxBinder),
            new PropertyMetadata(false));

    /// <summary>
    /// 双向桥接到 VM 字符串属性的附加属性。
    /// </summary>
    public static readonly DependencyProperty BoundPasswordProperty =
        DependencyProperty.RegisterAttached(
            "BoundPassword",
            typeof(string),
            typeof(PasswordBoxBinder),
            // 默认值取 null（而非 string.Empty）：密码字段常以空串初始化，若默认即为空串，
            // 绑定将空串写入附加属性时值未变化、不会触发 OnBoundPasswordChanged，导致
            // PasswordChanged 订阅永不挂接、控件→VM 回写失效。null 默认确保「设为空串」
            // 也是一次真实变更，从而可靠挂接桥接订阅。
            new FrameworkPropertyMetadata(
                null,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnBoundPasswordChanged));

    public static string GetBoundPassword(DependencyObject d)
    {
        return (string)d.GetValue(BoundPasswordProperty);
    }

    public static void SetBoundPassword(DependencyObject d, string value)
    {
        d.SetValue(BoundPasswordProperty, value);
    }

    private static bool GetIsUpdating(DependencyObject d)
    {
        return (bool)d.GetValue(IsUpdatingProperty);
    }

    private static void SetIsUpdating(DependencyObject d, bool value)
    {
        d.SetValue(IsUpdatingProperty, value);
    }

    private static void OnBoundPasswordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not PasswordBox passwordBox)
        {
            return;
        }

        // 首次附加时订阅 PasswordChanged（仅订阅一次，避免重复挂接）。
        passwordBox.PasswordChanged -= OnPasswordChanged;
        passwordBox.PasswordChanged += OnPasswordChanged;

        // 由控件回写触发的本次变更，跳过对控件的再次回写以防循环。
        if (GetIsUpdating(passwordBox))
        {
            return;
        }

        var newValue = e.NewValue as string ?? string.Empty;

        // 仅当与控件当前值不同才回写控件（VM 置空时清空控件内容）。
        if (!string.Equals(passwordBox.Password, newValue, System.StringComparison.Ordinal))
        {
            passwordBox.Password = newValue;
        }
    }

    private static void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not PasswordBox passwordBox)
        {
            return;
        }

        SetIsUpdating(passwordBox, true);
        try
        {
            SetBoundPassword(passwordBox, passwordBox.Password);
        }
        finally
        {
            SetIsUpdating(passwordBox, false);
        }
    }
}

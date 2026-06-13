using System.Globalization;
using System.Windows.Data;

namespace Orderly.App.Converters;

/// <summary>
/// 将安全审计事件类型（数据层返回的稳定不变量名，如 <c>"LoginSucceeded"</c>）映射为面向用户的中文展示文案
/// （任务 18.5 / Req 9.1）。
///
/// <para>数据层 <c>SecurityAuditService.KindToLabel</c> 明确「面向用户的中文展示映射由 ViewModel / 视图层负责」，
/// 故此处在我的页「账户安全 / 登录记录」卡内承担该映射；未识别的取值原样回显，避免丢失信息。</para>
/// </summary>
public sealed class SecurityAuditKindLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var kind = value?.ToString();
        return kind switch
        {
            "LoginSucceeded" => "登录成功",
            "LoginFailed" => "登录失败",
            "AccountLockedOut" => "账户锁定",
            "CredentialChanged" => "凭证变更",
            "MemberCreated" => "成员创建",
            "MemberPasswordReset" => "重置密码",
            "MemberDisabled" => "停用成员",
            "MemberDeleted" => "删除成员",
            _ => kind ?? string.Empty
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

namespace Orderly.App.ViewModels;

/// <summary>
/// 主密码修改表单的实时校验状态值对象（设计文档 §8.5）。
/// 仅承载校验结果，提交门槛由 <see cref="CanSubmit"/> 计算，强度偏弱仅作警告不参与门槛。
/// </summary>
/// <param name="IsCurrentProvided">当前密码已提供（非空）。</param>
/// <param name="IsNewLengthValid">新密码长度 >= 8（提交硬门槛之一）。</param>
/// <param name="IsConfirmMatch">两次输入一致。</param>
/// <param name="IsStrengthWeak">长度达标但强度低于 Fair，仅触发警告，不阻断提交。</param>
/// <param name="Message">UI 实时提示文案（中文）。</param>
public sealed record PasswordValidationState(
    bool IsCurrentProvided,
    bool IsNewLengthValid,
    bool IsConfirmMatch,
    bool IsStrengthWeak,
    string? Message)
{
    // 提交门槛（Req 8.2 / Property 2）：仅要求「当前非空 + 新密码长度 >= 8 + 两次一致」；
    // 新密码强度不作为提交的硬性门槛，强度偏弱只产出警告文案。
    public bool CanSubmit => IsCurrentProvided && IsNewLengthValid && IsConfirmMatch;
}

/// <summary>
/// PIN 修改表单的实时校验状态值对象（设计文档 §8.5）。
/// </summary>
/// <param name="IsCurrentProvided">当前 PIN 已提供（非空）。</param>
/// <param name="IsNewDigitsValid">新 PIN 为恰好 6 位纯数字。</param>
/// <param name="IsConfirmMatch">两次输入一致。</param>
/// <param name="Message">UI 实时提示文案（中文）。</param>
public sealed record PinValidationState(
    bool IsCurrentProvided,
    bool IsNewDigitsValid,
    bool IsConfirmMatch,
    string? Message)
{
    // 提交门槛（Req 8.4 / Property 2）：当前 PIN 非空 + 新 PIN 恰好 6 位纯数字 + 两次一致。
    public bool CanSubmit => IsCurrentProvided && IsNewDigitsValid && IsConfirmMatch;
}

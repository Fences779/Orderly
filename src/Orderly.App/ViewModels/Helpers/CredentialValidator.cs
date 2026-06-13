using System.Text.RegularExpressions;

namespace Orderly.App.ViewModels.Helpers;

/// <summary>
/// 凭证修改表单的实时校验纯函数（设计 §8.5 / §9.3，Req 8.1~8.4、8.10）。
///
/// <para>主密码与 PIN 的校验逻辑抽离为无副作用的静态方法，便于属性 / 单元测试覆盖，
/// 也供「我的页」ViewModel 在 <c>*Input</c> 属性变更时调用以产出
/// <see cref="PasswordValidationState"/> / <see cref="PinValidationState"/>。</para>
///
/// <para>提交门槛已放宽（OQ-8）：主密码仅以「当前非空 + 新密码长度 &gt;= 8 + 两次一致」为硬门槛，
/// 新密码强度<b>不</b>作为提交门槛，强度低于 <see cref="PasswordStrength.Fair"/> 时仅置
/// <see cref="PasswordValidationState.IsStrengthWeak"/> 并产出偏弱警告文案。</para>
///
/// <para>校验在内存中即时进行，不记录、不持久化、不出现在日志（P4 安全底线）。</para>
/// </summary>
public static class CredentialValidator
{
    /// <summary>新密码长度硬门槛（设计 §9.3）。</summary>
    public const int MinPasswordLength = 8;

    // PIN 固定 6 位纯数字（OQ-5）；预编译正则避免重复构造。
    private static readonly Regex PinPattern = new("^[0-9]{6}$", RegexOptions.Compiled);

    /// <summary>
    /// 重算主密码修改表单的校验状态（设计 §9.3 <c>RecomputeMasterPasswordValidation</c>）。
    /// </summary>
    /// <param name="currentPassword">当前主密码输入。</param>
    /// <param name="newPassword">新主密码输入。</param>
    /// <param name="confirmPassword">确认新主密码输入。</param>
    /// <returns>承载校验结果与中文实时提示文案的 <see cref="PasswordValidationState"/>。</returns>
    public static PasswordValidationState RecomputeMasterPasswordValidation(
        string? currentPassword,
        string? newPassword,
        string? confirmPassword)
    {
        bool isCurrentProvided = !string.IsNullOrEmpty(currentPassword);

        PasswordStrength strength = PasswordStrengthEvaluator.Evaluate(newPassword ?? string.Empty);

        // 唯一长度门槛：新密码长度 >= 8。
        bool isNewLengthValid = (newPassword?.Length ?? 0) >= MinPasswordLength;

        // 强度偏弱仅在长度达标时作为警告，不参与 CanSubmit。
        bool isStrengthWeak = isNewLengthValid && strength < PasswordStrength.Fair;

        // 两次一致且新密码非空，避免空对空被判为一致。
        bool isConfirmMatch = !string.IsNullOrEmpty(newPassword)
            && string.Equals(newPassword, confirmPassword, System.StringComparison.Ordinal);

        // 文案优先级：阻断项优先，其次「强度偏弱」警告（不阻断），最后通过态。
        string message;
        if (!isCurrentProvided)
        {
            message = "请先输入当前密码";
        }
        else if (!isNewLengthValid)
        {
            message = "新密码至少 8 位";
        }
        else if (!isConfirmMatch)
        {
            message = "两次输入的新密码不一致";
        }
        else if (isStrengthWeak)
        {
            message = "密码强度偏弱，建议混合大小写/数字/符号（不影响提交）";
        }
        else
        {
            message = "可以提交";
        }

        return new PasswordValidationState(
            isCurrentProvided,
            isNewLengthValid,
            isConfirmMatch,
            isStrengthWeak,
            message);
    }

    /// <summary>
    /// 重算 PIN 修改表单的校验状态（设计 §9.3 <c>RecomputePinValidation</c>）。
    /// </summary>
    /// <param name="currentPin">当前 PIN 输入。</param>
    /// <param name="newPin">新 PIN 输入。</param>
    /// <param name="confirmPin">确认新 PIN 输入。</param>
    /// <returns>承载校验结果与中文实时提示文案的 <see cref="PinValidationState"/>。</returns>
    public static PinValidationState RecomputePinValidation(
        string? currentPin,
        string? newPin,
        string? confirmPin)
    {
        bool isCurrentProvided = !string.IsNullOrEmpty(currentPin);

        // 恰好 6 位纯数字。
        bool isNewDigitsValid = !string.IsNullOrEmpty(newPin) && PinPattern.IsMatch(newPin);

        bool isConfirmMatch = !string.IsNullOrEmpty(newPin)
            && string.Equals(newPin, confirmPin, System.StringComparison.Ordinal);

        string message;
        if (!isCurrentProvided)
        {
            message = "请先输入当前 PIN";
        }
        else if (!isNewDigitsValid)
        {
            message = "PIN 必须为 6 位数字";
        }
        else if (!isConfirmMatch)
        {
            message = "两次输入的 PIN 不一致";
        }
        else
        {
            message = "可以提交";
        }

        return new PinValidationState(
            isCurrentProvided,
            isNewDigitsValid,
            isConfirmMatch,
            message);
    }
}

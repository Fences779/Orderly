using Orderly.App.ViewModels;
using Orderly.App.ViewModels.Helpers;
using Xunit;

namespace Orderly.Tests.Settings;

/// <summary>
/// 凭证表单实时校验的文案与边界单元测试（设计 §9.3，Req 8.1 / 8.3 / 8.10）。
///
/// <para>覆盖主密码与 PIN 校验的四类分支（缺当前凭证 / 新值过短或位数错 / 两次不一致 / 全部通过）的
/// <see cref="PasswordValidationState.CanSubmit"/> 与 <c>Message</c> 中文文案；并验证主密码长度达标
/// 但强度偏弱（如 <c>"aaaaaaaa"</c>）时 <see cref="PasswordValidationState.IsStrengthWeak"/> 为真且
/// <see cref="PasswordValidationState.CanSubmit"/> 仍为真（强度警告不阻断提交）。</para>
/// </summary>
public sealed class CredentialValidatorMessageTests
{
    private const string ValidCurrent = "OldPass123!";

    // ---------- 主密码：分支文案与 CanSubmit ----------

    [Fact]
    public void Master_missing_current_blocks_with_current_message()
    {
        PasswordValidationState state =
            CredentialValidator.RecomputeMasterPasswordValidation(
                currentPassword: "",
                newPassword: "Abcdef12",
                confirmPassword: "Abcdef12");

        Assert.False(state.IsCurrentProvided);
        Assert.False(state.CanSubmit);
        Assert.Equal("请先输入当前密码", state.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ab1!")]
    [InlineData("Abc12")]   // 长度 5 < 8
    [InlineData("Abcdef1")] // 长度 7 < 8
    public void Master_short_new_password_blocks_with_length_message(string newPassword)
    {
        PasswordValidationState state =
            CredentialValidator.RecomputeMasterPasswordValidation(
                currentPassword: ValidCurrent,
                newPassword: newPassword,
                confirmPassword: newPassword);

        Assert.False(state.IsNewLengthValid);
        Assert.False(state.CanSubmit);
        Assert.Equal("新密码至少 8 位", state.Message);
    }

    [Fact]
    public void Master_confirm_mismatch_blocks_with_mismatch_message()
    {
        PasswordValidationState state =
            CredentialValidator.RecomputeMasterPasswordValidation(
                currentPassword: ValidCurrent,
                newPassword: "Abcdef12",
                confirmPassword: "Abcdef13");

        Assert.True(state.IsNewLengthValid);
        Assert.False(state.IsConfirmMatch);
        Assert.False(state.CanSubmit);
        Assert.Equal("两次输入的新密码不一致", state.Message);
    }

    [Fact]
    public void Master_all_valid_strong_password_can_submit()
    {
        PasswordValidationState state =
            CredentialValidator.RecomputeMasterPasswordValidation(
                currentPassword: ValidCurrent,
                newPassword: "Abc!2xyzQ9",
                confirmPassword: "Abc!2xyzQ9");

        Assert.True(state.IsCurrentProvided);
        Assert.True(state.IsNewLengthValid);
        Assert.True(state.IsConfirmMatch);
        Assert.False(state.IsStrengthWeak);
        Assert.True(state.CanSubmit);
        Assert.Equal("可以提交", state.Message);
    }

    [Fact]
    public void Master_length_ok_but_weak_strength_warns_yet_can_submit()
    {
        // 长度 >= 8 但强度偏弱（单一字符类别 + 长重复串）：警告不阻断提交。
        PasswordValidationState state =
            CredentialValidator.RecomputeMasterPasswordValidation(
                currentPassword: ValidCurrent,
                newPassword: "aaaaaaaa",
                confirmPassword: "aaaaaaaa");

        Assert.True(state.IsNewLengthValid);
        Assert.True(state.IsConfirmMatch);
        Assert.True(state.IsStrengthWeak);
        Assert.True(state.CanSubmit);
        Assert.Equal("密码强度偏弱，建议混合大小写/数字/符号（不影响提交）", state.Message);
    }

    // ---------- PIN：分支文案与 CanSubmit ----------

    [Fact]
    public void Pin_missing_current_blocks_with_current_message()
    {
        PinValidationState state =
            CredentialValidator.RecomputePinValidation(
                currentPin: "",
                newPin: "123456",
                confirmPin: "123456");

        Assert.False(state.IsCurrentProvided);
        Assert.False(state.CanSubmit);
        Assert.Equal("请先输入当前 PIN", state.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("12345")]    // 位数不足
    [InlineData("1234567")]  // 位数过多
    [InlineData("12345a")]   // 含非数字
    [InlineData("12 456")]   // 含空格
    public void Pin_invalid_digits_blocks_with_digits_message(string newPin)
    {
        PinValidationState state =
            CredentialValidator.RecomputePinValidation(
                currentPin: "000000",
                newPin: newPin,
                confirmPin: newPin);

        Assert.False(state.IsNewDigitsValid);
        Assert.False(state.CanSubmit);
        Assert.Equal("PIN 必须为 6 位数字", state.Message);
    }

    [Fact]
    public void Pin_confirm_mismatch_blocks_with_mismatch_message()
    {
        PinValidationState state =
            CredentialValidator.RecomputePinValidation(
                currentPin: "000000",
                newPin: "123456",
                confirmPin: "654321");

        Assert.True(state.IsNewDigitsValid);
        Assert.False(state.IsConfirmMatch);
        Assert.False(state.CanSubmit);
        Assert.Equal("两次输入的 PIN 不一致", state.Message);
    }

    [Fact]
    public void Pin_all_valid_can_submit()
    {
        PinValidationState state =
            CredentialValidator.RecomputePinValidation(
                currentPin: "000000",
                newPin: "123456",
                confirmPin: "123456");

        Assert.True(state.IsCurrentProvided);
        Assert.True(state.IsNewDigitsValid);
        Assert.True(state.IsConfirmMatch);
        Assert.True(state.CanSubmit);
        Assert.Equal("可以提交", state.Message);
    }
}

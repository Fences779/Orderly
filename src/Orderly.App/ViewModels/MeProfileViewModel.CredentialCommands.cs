using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Orderly.Core.Models;
using Orderly.Core.Services;

namespace Orderly.App.ViewModels;

/// <summary>
/// 「我的页」凭证修改命令与会话转移接线（任务 14.3，design §8.5 / §9.6，Req 8 / 16）。
///
/// <para>命令清单：修改当前账号主密码、修改当前账号 PIN。各命令 <c>CanExecute</c> 复用
/// <see cref="PasswordValidationState.CanSubmit"/> / <see cref="PinValidationState.CanSubmit"/>
/// （强度仅作偏弱警告，<b>不</b>参与提交门槛，Req 8.2 / 8.10）。</para>
///
/// <para>命令体调用 <see cref="ILocalAccountManagementService.ChangeCurrentMasterPasswordAsync"/> /
/// <see cref="ILocalAccountManagementService.ChangeCurrentPinAsync"/>；命令完成（无论成功失败）即清空
/// 相关凭证输入框（Req 8.8 / 14.4 / P4，明文不残留）；结果在卡片内就地反馈（<see cref="CredentialChangeStatus"/>，
/// 不使用离开页 Toast，Req 8.7）。</para>
///
/// <para>命令成功后调用 <see cref="ICredentialChangeSessionCoordinator.OnCredentialChangeCompleted"/>
/// 触发会话转移（主密码 → 强制登出、PIN → <c>PendingPinUnlock</c>，Req 16.1 / 16.2）；
/// 失败或取消则不触碰会话状态（Req 16.3）。</para>
/// </summary>
public partial class MeProfileViewModel
{
    /// <summary>
    /// 凭证修改卡片内就地反馈文案（成功 / 失败）。Req 8.7：在卡片内就地反馈，不使用离开页 Toast。
    /// </summary>
    [ObservableProperty]
    private string credentialChangeStatus = string.Empty;

    // ── 命令 CanExecute 谓词（复用实时校验 CanSubmit，强度仅警告不阻断，§8.5 / Req 8.2 / 8.4 / 8.10） ──

    private bool CanChangeCurrentMasterPassword()
        => _accountService is not null && !IsBusy && MasterPasswordValidation.CanSubmit;

    private bool CanChangeCurrentPin()
        => _accountService is not null && !IsBusy && PinValidation.CanSubmit;

    // ── 命令 ──

    /// <summary>
    /// 修改当前账号主密码（Req 8.1 / 8.2 / 8.7 / 8.8 / 16.1）：成功后强制登出，要求用新主密码重新登录。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanChangeCurrentMasterPassword))]
    private async Task ChangeCurrentMasterPasswordAsync()
    {
        if (_accountService is null || !MasterPasswordValidation.CanSubmit)
        {
            return;
        }

        await RunCredentialChangeAsync(
            kind: CredentialChangeKind.MasterPassword,
            successMessage: "当前账号主密码已修改，将要求使用新主密码重新登录",
            errorPrefix: "修改主密码失败",
            action: ct => _accountService.ChangeCurrentMasterPasswordAsync(
                CurrentMasterPasswordInput, NewCurrentMasterPasswordInput, ct),
            clearInputs: ClearMasterPasswordInputs);
    }

    /// <summary>
    /// 修改当前账号 PIN（Req 8.3 / 8.4 / 8.7 / 8.8 / 16.2）：成功后锁定进入 <c>PendingPinUnlock</c>，不强制登出。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanChangeCurrentPin))]
    private async Task ChangeCurrentPinAsync()
    {
        if (_accountService is null || !PinValidation.CanSubmit)
        {
            return;
        }

        await RunCredentialChangeAsync(
            kind: CredentialChangeKind.Pin,
            successMessage: "当前账号 PIN 已修改，将锁定并要求使用新 PIN 解锁",
            errorPrefix: "修改 PIN 失败",
            action: ct => _accountService.ChangeCurrentPinAsync(
                CurrentPinInput.Trim(), NewCurrentPinInput.Trim(), ct),
            clearInputs: ClearPinInputs);
    }

    // ── 内部帮助 ──

    /// <summary>
    /// 统一执行凭证修改后端动作：忙碌门控 + 就地结果反馈（Req 8.7）+ 命令完成即清空凭证输入（Req 8.8 / 14.4）+
    /// 仅成功时触发会话转移（Req 16.1 / 16.2 / 16.3）。
    /// <para>无论成功失败，凭证输入均在 <c>finally</c> 中清空，确保明文不残留（P4）。会话转移仅在动作成功
    /// 完成后调用一次；失败 / 取消不触碰会话状态。</para>
    /// </summary>
    private async Task RunCredentialChangeAsync(
        CredentialChangeKind kind,
        string successMessage,
        string errorPrefix,
        Func<CancellationToken, Task> action,
        Action clearInputs)
    {
        if (IsBusy)
        {
            return;
        }

        var result = CredentialChangeResult.Failed;
        try
        {
            IsBusy = true;
            await action(CancellationToken.None);
            result = CredentialChangeResult.Success;
            CredentialChangeStatus = successMessage;
        }
        catch (Exception ex)
        {
            CredentialChangeStatus = $"{errorPrefix}：{ex.Message}";
        }
        finally
        {
            // Req 8.8 / 14.4 / P4：命令完成（无论成功失败）即清空相关凭证输入，明文即用即清。
            clearInputs();
            IsBusy = false;
        }

        // Req 16.1 / 16.2：仅成功时触发会话转移（主密码 → 强制登出、PIN → PendingPinUnlock）。
        // Req 16.3：失败 / 取消保持会话状态不变。协调器可空时（完整 DI 见 21.1）跳过。
        if (result == CredentialChangeResult.Success)
        {
            _credentialSession?.OnCredentialChangeCompleted(kind, result);
        }
    }

    /// <summary>清空主密码修改表单的三项输入（当前 / 新 / 确认）。</summary>
    private void ClearMasterPasswordInputs()
    {
        CurrentMasterPasswordInput = string.Empty;
        NewCurrentMasterPasswordInput = string.Empty;
        ConfirmNewMasterPasswordInput = string.Empty;
    }

    /// <summary>清空 PIN 修改表单的三项输入（当前 / 新 / 确认）。</summary>
    private void ClearPinInputs()
    {
        CurrentPinInput = string.Empty;
        NewCurrentPinInput = string.Empty;
        ConfirmNewPinInput = string.Empty;
    }
}

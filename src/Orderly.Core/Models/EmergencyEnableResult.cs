namespace Orderly.Core.Models;

/// <summary>
/// Owner 紧急启用（<c>TryEmergencyEnable</c>）的结果（需求 17.1 / 17.2，design §9.7）。
///
/// 成功表示已进入 <see cref="SessionPermissionMode.Restricted_Permission"/> 受限权限模式；
/// 失败时 <see cref="ErrorMessage"/> 携带面向用户的中文错误提示，且会话权限模式保持不变。
/// </summary>
public sealed record EmergencyEnableResult
{
    private EmergencyEnableResult(bool succeeded, string? errorMessage)
    {
        Succeeded = succeeded;
        ErrorMessage = errorMessage;
    }

    /// <summary>是否紧急启用成功并进入受限权限模式。</summary>
    public bool Succeeded { get; }

    /// <summary>失败时的中文错误提示；成功时为 <see langword="null"/>。</summary>
    public string? ErrorMessage { get; }

    /// <summary>构造成功结果（已进入受限权限模式）。</summary>
    public static EmergencyEnableResult Success() => new(true, null);

    /// <summary>构造失败结果，携带面向用户的中文错误提示。</summary>
    /// <param name="errorMessage">中文错误提示。</param>
    public static EmergencyEnableResult Failure(string errorMessage) => new(false, errorMessage);
}

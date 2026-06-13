namespace Orderly.Core.Models;

/// <summary>
/// 被修改的凭证种类，用于决定凭证修改成功后的会话转移方式（design §9.6 / Req 16）。
/// </summary>
public enum CredentialChangeKind
{
    /// <summary>主密码：修改成功后强制登出并要求用新主密码重新登录。</summary>
    MasterPassword = 0,

    /// <summary>PIN：修改成功后锁定进入待 PIN 解锁状态，不强制登出。</summary>
    Pin = 1,
}

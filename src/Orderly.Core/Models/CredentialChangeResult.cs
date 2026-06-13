namespace Orderly.Core.Models;

/// <summary>
/// 凭证修改命令的最终结果，决定是否触发会话转移（design §9.6 / Req 16）。
/// </summary>
public enum CredentialChangeResult
{
    /// <summary>凭证修改成功，须按凭证种类执行会话转移。</summary>
    Success = 0,

    /// <summary>凭证修改失败（如校验未过 / 持久化异常），会话状态保持不变。</summary>
    Failed = 1,

    /// <summary>凭证修改被用户取消，会话状态保持不变。</summary>
    Cancelled = 2,
}

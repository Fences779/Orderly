using Orderly.Core.Models;

namespace Orderly.Core.Services;

/// <summary>
/// 凭证修改后的会话转移协调器（design §9.6 / Req 16 / Property 13）。
///
/// 凭证修改成功后，会话须以与变更凭证相匹配的方式被重新验证，确保新凭证立即生效且不残留旧凭证会话：
/// <list type="bullet">
///   <item>主密码修改成功 → 强制登出，要求用新主密码重新登录（Req 16.1）。</item>
///   <item>PIN 修改成功 → 锁定进入 <see cref="SessionLockState.PendingPinUnlock"/>，不强制登出（Req 16.2）。</item>
///   <item>修改失败或取消 → 会话状态保持不变，既不登出也不锁定（Req 16.3）。</item>
/// </list>
///
/// 成功路径会先经统一安全审计记录一条 <see cref="SecurityEventType.CredentialChange"/> 事件，
/// 仅记录"哪种凭证被修改"等元数据，绝不写入明文密码 / PIN（Req 16.4 / P4 / Property 7）。
/// 会话转移复用既有的手动锁定与会话上下文机制，不新造并行状态机。
/// </summary>
public interface ICredentialChangeSessionCoordinator
{
    /// <summary>
    /// 在凭证修改命令完成后调用，按（凭证种类, 结果）唯一决定会话后置状态。
    /// </summary>
    /// <param name="kind">被修改的凭证种类。</param>
    /// <param name="result">凭证修改命令的最终结果。</param>
    void OnCredentialChangeCompleted(CredentialChangeKind kind, CredentialChangeResult result);
}

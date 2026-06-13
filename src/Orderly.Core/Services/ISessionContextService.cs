using Orderly.Core.Models;

namespace Orderly.Core.Services;

public interface ISessionContextService
{
    event EventHandler? SessionChanged;

    LocalSessionContext? Current { get; }
    bool IsSignedIn { get; }
    bool IsDataKeyAvailable { get; }

    /// <summary>当前会话权限模式，默认 <see cref="SessionPermissionMode.Normal"/>。</summary>
    SessionPermissionMode CurrentPermissionMode { get; }

    /// <summary>是否处于受限权限模式（只读标志，由 <see cref="CurrentPermissionMode"/> 派生）。</summary>
    bool IsRestrictedPermissionMode => CurrentPermissionMode == SessionPermissionMode.Restricted_Permission;

    void SetCurrent(LocalSessionContext session);
    void SuspendDataKey();
    bool TryRestoreDataKey(string accountId);
    void Clear();

    /// <summary>设置当前会话权限模式（受限权限模式的进入/退出由账户与会话服务层在后续任务中驱动）。</summary>
    void SetPermissionMode(SessionPermissionMode mode);
}

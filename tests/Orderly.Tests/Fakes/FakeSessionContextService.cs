using Orderly.Core.Models;
using Orderly.Core.Services;

namespace Orderly.Tests.Fakes;

/// <summary>
/// 未认证场景的会话上下文桩：始终没有当前会话。
/// 目录读取（ListAccountDirectoryAsync）属于认证前路径，不依赖会话。
/// </summary>
internal sealed class FakeSessionContextService : ISessionContextService
{
    public event EventHandler? SessionChanged;

    public LocalSessionContext? Current => null;
    public bool IsSignedIn => false;
    public bool IsDataKeyAvailable => false;

    public void SetCurrent(LocalSessionContext session) => SessionChanged?.Invoke(this, EventArgs.Empty);
    public void SuspendDataKey() { }
    public bool TryRestoreDataKey(string accountId) => false;
    public void Clear() { }
}

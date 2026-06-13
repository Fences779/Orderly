using Orderly.Core.Models;
using Orderly.Core.Services;

namespace Orderly.Tests.Fakes;

/// <summary>
/// 已认证场景的会话上下文桩：提供一个固定的 32 字节数据密钥，
/// 用于驱动 <see cref="Orderly.Data.Services.FieldEncryptionService"/> 的
/// AES-GCM 字段加解密路径（<c>RequireCurrentDataKey</c> 需要可用且长度为 32 的密钥）。
///
/// 可通过 <see cref="SuspendDataKey"/> 模拟"数据密钥不可用"以观察 fail-closed 行为。
/// </summary>
internal sealed class FakeDataKeySessionContextService : ISessionContextService
{
    private bool _dataKeyAvailable;
    private SessionPermissionMode _permissionMode = SessionPermissionMode.Normal;

    public FakeDataKeySessionContextService(byte[] dataKey)
    {
        Current = new LocalSessionContext
        {
            AccountId = "test-account",
            Username = "tester",
            DisplayName = "Tester",
            Role = LocalAccountRole.Owner,
            DatabasePath = string.Empty,
            DataKey = dataKey,
        };
        _dataKeyAvailable = true;
    }

    public event EventHandler? SessionChanged;

    public LocalSessionContext? Current { get; private set; }
    public bool IsSignedIn => Current is not null;
    public bool IsDataKeyAvailable => _dataKeyAvailable;
    public SessionPermissionMode CurrentPermissionMode => _permissionMode;

    public void SetPermissionMode(SessionPermissionMode mode) => _permissionMode = mode;

    public void SetCurrent(LocalSessionContext session)
    {
        Current = session;
        _dataKeyAvailable = true;
        SessionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SuspendDataKey() => _dataKeyAvailable = false;

    public bool TryRestoreDataKey(string accountId)
    {
        _dataKeyAvailable = true;
        return true;
    }

    public void Clear()
    {
        Current = null;
        _dataKeyAvailable = false;
        SessionChanged?.Invoke(this, EventArgs.Empty);
    }
}

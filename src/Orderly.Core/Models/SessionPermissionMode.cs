namespace Orderly.Core.Models;

/// <summary>
/// 会话级权限模式。以会话状态标志表达"受限权限模式"，而非新增账号角色（最小作用域）。
/// </summary>
public enum SessionPermissionMode
{
    /// <summary>常规会话：按账号角色拥有完整权限。</summary>
    Normal = 0,

    /// <summary>
    /// 受限权限模式：被停用的 Owner 凭正确 6 位 PIN 紧急启用后所处模式，
    /// 仅可执行核心紧急操作（如数据备份），禁止查看现金流等机密/隐私数据。
    /// </summary>
    Restricted_Permission = 1
}

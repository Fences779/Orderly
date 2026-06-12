using Orderly.Core.Services;

namespace Orderly.Data.Services;

/// <summary>
/// Fail-closed guard for the production field-encryption / write path.
///
/// 检测被注入的 <see cref="IFieldEncryptionService"/> 是否为空操作（明文透传）实现
/// （如 <see cref="PassthroughFieldEncryptionService"/>）。空操作加密器若进入生产写入路径，
/// 会导致本应加密的敏感字段以明文落盘（静默关闭静态数据加密）。
///
/// 本守卫提供两种 fail-closed 用法，清晰区分生产路径与 QA/测试专用路径：
/// <list type="bullet">
///   <item><see cref="EnsureProductionGrade"/>：供组合根在启动期断言；检测到空操作加密器即抛出
///   明确异常以拒绝启动（fail-closed startup）。</item>
///   <item><see cref="ProtectFieldValue"/>：供字段加密写入路径在落盘前做兜底；当加密器非生产级实现时，
///   绝不回写明文，而是写入中性的脱敏占位符（fail-closed write），避免敏感数据明文落盘。</item>
/// </list>
///
/// QA/测试专用路径（<c>tools/qa</c>）一律装配真实 AES-GCM 实现
/// （<see cref="FieldEncryptionService"/>），因此不受本守卫影响。
/// </summary>
public static class FieldEncryptionGuard
{
    /// <summary>
    /// 当非生产级（空操作）加密器进入写入路径时，写入此中性脱敏占位符以替代明文，确保敏感数据绝不明文落盘。
    /// 该占位符不包含任何调用方传入的明文。
    /// </summary>
    internal const string RedactedFieldValue = "[REDACTED:insecure-field-encryptor]";

    /// <summary>
    /// 判定给定加密器是否为生产级（真实 AES-GCM）实现。
    /// 空操作/明文透传实现（<see cref="PassthroughFieldEncryptionService"/>）返回 <c>false</c>。
    /// </summary>
    public static bool IsProductionGrade(IFieldEncryptionService fieldEncryptionService)
    {
        ArgumentNullException.ThrowIfNull(fieldEncryptionService);
        return fieldEncryptionService is not PassthroughFieldEncryptionService;
    }

    /// <summary>
    /// 启动期 fail-closed 断言：要求生产路径注入的加密器为真实 AES-GCM 实现。
    /// 检测到空操作加密器时抛出 <see cref="InvalidOperationException"/> 以拒绝启动，避免明文落盘。
    /// </summary>
    /// <param name="fieldEncryptionService">组合根装配的字段加密器。</param>
    /// <param name="context">用于异常信息的上下文（如组合根方法名）。</param>
    public static void EnsureProductionGrade(IFieldEncryptionService fieldEncryptionService, string context)
    {
        if (!IsProductionGrade(fieldEncryptionService))
        {
            throw new InvalidOperationException(
                $"{context}: 生产路径被注入了空操作字段加密器（{nameof(PassthroughFieldEncryptionService)}）。"
                + "为避免敏感字段以明文落盘，已 fail-closed 拒绝启动。请装配真实的 AES-GCM 字段加密实现。");
        }
    }

    /// <summary>
    /// 写入路径兜底 fail-closed：当加密器为生产级实现时返回 <paramref name="encrypt"/> 计算出的密文；
    /// 否则（空操作加密器）绝不回写明文，返回中性脱敏占位符 <see cref="RedactedFieldValue"/>。
    /// </summary>
    /// <param name="fieldEncryptionService">写入路径使用的字段加密器。</param>
    /// <param name="encrypt">仅当加密器为生产级实现时才会被调用的真实加密委托。</param>
    internal static string ProtectFieldValue(IFieldEncryptionService fieldEncryptionService, Func<string> encrypt)
    {
        ArgumentNullException.ThrowIfNull(encrypt);
        return IsProductionGrade(fieldEncryptionService) ? encrypt() : RedactedFieldValue;
    }
}

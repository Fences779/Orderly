using Orderly.Core.Models;

namespace Orderly.Core.Services;

/// <summary>
/// 头像存储服务抽象（需求 11.4 / 6.1 / 6.7，design BC-4）。
///
/// 负责头像图片的校验（格式 / 大小 / 可解码，见 <see cref="AvatarConstraints"/>）、
/// 解码再编码以剥离 EXIF、缩放为方形缩略图、写入 app 数据目录下受保护的 <c>avatars/</c>
/// 子目录并施加 <c>HardenFile</c> 加固，仅以相对引用键对外暴露位置。
///
/// 安全约束（P0/P4）：头像为非机密展示资源，文件系统隔离 + 目录加固即可，不进 SQLCipher 库；
/// 解析路径必须落在受保护子目录内，拒绝越界绝对路径（Property 8）。
/// </summary>
public interface IAvatarStorageService
{
    /// <summary>
    /// 校验 → 解码 → 剥离 EXIF → 缩放方形缩略图 → 写入受保护目录；返回相对引用键。
    /// 任一校验未通过（格式不受支持 / 超过大小上限 / 不可解码）时抛验证异常，调用方须保留原头像。
    /// </summary>
    /// <param name="accountId">目标账号标识，用于派生头像文件名。</param>
    /// <param name="sourceImagePath">用户所选源图片的路径。</param>
    Task<AvatarReference> SaveAvatarAsync(string accountId, string sourceImagePath, CancellationToken ct = default);

    /// <summary>
    /// 将相对引用键解析为可加载的绝对路径（供 ImageSource 使用）。
    /// 解析结果必须位于 app 数据目录受保护子目录内；引用为 null 或越界时返回 null。
    /// </summary>
    string? ResolveAvatarPath(AvatarReference? reference);

    /// <summary>移除指定账号的已存头像文件（恢复默认占位头像）。</summary>
    Task RemoveAvatarAsync(string accountId, CancellationToken ct = default);
}

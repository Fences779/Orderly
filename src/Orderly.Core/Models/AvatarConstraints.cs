namespace Orderly.Core.Models;

/// <summary>
/// 头像校验约束常量（需求 6.1 / 6.6 / Property 16）。
///
/// 校验通过判定：<c>Accepted ⟺ (格式 ∈ {JPG, PNG, WebP}) ∧ (文件大小 ≤ 5MB) ∧ (可成功解码)</c>。
/// 格式按解码后的真实编码判定，不仅看扩展名。
/// </summary>
public static class AvatarConstraints
{
    /// <summary>仅接受的图片格式（按解码后真实编码判定，不仅看扩展名）。</summary>
    public static readonly string[] AcceptedFormats = { "JPG", "PNG", "WebP" };

    /// <summary>单张文件大小硬上限：5MB。</summary>
    public const long MaxFileSizeBytes = 5L * 1024 * 1024;

    /// <summary>方形缩略图边长（像素）。</summary>
    public const int ThumbnailEdge = 256;
}

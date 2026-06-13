namespace Orderly.Core.Models;

/// <summary>
/// 头像引用：以相对键形式存储，真实文件落在 app 数据目录下的受保护子目录。
/// 例：<c>"avatars/{accountId}.png"</c>，由 <c>IAvatarStorageService</c> 解析为绝对路径。
/// 仅存相对键、不存绝对路径，避免越界访问（需求 6.7 / Property 8）。
/// </summary>
public sealed record AvatarReference(string RelativeKey);

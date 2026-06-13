namespace Orderly.Core.Services;

/// <summary>
/// 头像校验异常（需求 6.6 / Property 16）。
///
/// 当所选图片格式不受支持、不可解码或超过大小上限（见 <c>AvatarConstraints</c>）时，
/// <see cref="IAvatarStorageService.SaveAvatarAsync"/> 抛出本异常。调用方（MeProfileViewModel）
/// 捕获后须保留原头像（不更新 <c>AvatarReference</c>）并就地提示「图片无效或过大」。
/// </summary>
public sealed class AvatarValidationException : Exception
{
    public AvatarValidationException(string message)
        : base(message)
    {
    }

    public AvatarValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

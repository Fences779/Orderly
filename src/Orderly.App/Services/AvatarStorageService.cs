using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Orderly.Core.Models;
using Orderly.Core.Services;
using Orderly.Data.Sqlite;

namespace Orderly.App.Services;

/// <summary>
/// 头像存储服务实现（design BC-4，需求 6.1 / 6.2 / 6.7 / 11.4）。
///
/// 处理流水线：按 <see cref="AvatarConstraints"/> 校验真实编码格式（按文件头魔数判定，不仅看扩展名）/
/// 大小（≤5MB）/可解码 → 经 WPF <see cref="BitmapDecoder"/> 解码后重新编码以剥离 EXIF 元数据 →
/// 居中裁剪并缩放为方形缩略图（<see cref="AvatarConstraints.ThumbnailEdge"/>）→ 写入 app 数据目录下
/// 受保护的 <c>avatars/</c> 子目录并施加 <see cref="LocalDataFileSecurity.HardenFile"/> 加固。
///
/// 安全约束（P0/P4）：头像为非机密展示资源，仅以相对引用键对外暴露位置，不存绝对路径；
/// <see cref="ResolveAvatarPath"/> 仅解析到受保护子目录内，拒绝越界绝对路径（Property 8）。
/// </summary>
public sealed class AvatarStorageService : IAvatarStorageService
{
    private const string AvatarsDirectoryName = "avatars";

    // 输出统一为 PNG（重编码剥离 EXIF），文件名由 accountId 派生，便于覆盖式更新。
    private const string OutputExtension = ".png";

    private readonly Func<string> _appRootProvider;

    /// <summary>生产构造：app 数据根目录经 <see cref="DatabasePaths.GetAppRootPath"/> 解析。</summary>
    public AvatarStorageService()
        : this(DatabasePaths.GetAppRootPath)
    {
    }

    /// <summary>
    /// 测试 / 自定义构造：允许注入 app 数据根目录提供器，避免测试写入真实用户目录。
    /// </summary>
    public AvatarStorageService(Func<string> appRootProvider)
    {
        _appRootProvider = appRootProvider ?? throw new ArgumentNullException(nameof(appRootProvider));
    }

    /// <inheritdoc />
    public Task<AvatarReference> SaveAvatarAsync(string accountId, string sourceImagePath, CancellationToken ct = default)
    {
        // 全部 WPF 成像对象在同一后台线程内创建并消费，规避 DispatcherObject 线程亲和性问题。
        return Task.Run(() => SaveAvatarCore(accountId, sourceImagePath, ct), ct);
    }

    /// <inheritdoc />
    public string? ResolveAvatarPath(AvatarReference? reference)
    {
        if (reference is null || string.IsNullOrWhiteSpace(reference.RelativeKey))
        {
            return null;
        }

        var key = reference.RelativeKey;

        // 拒绝越界绝对/带根路径（Property 8）：仅接受相对引用键。
        if (Path.IsPathRooted(key))
        {
            return null;
        }

        var avatarsDirectory = Path.GetFullPath(GetAvatarsDirectoryPath());
        var appRoot = Path.GetFullPath(_appRootProvider());

        string candidate;
        try
        {
            // 引用键形如 "avatars/{accountId}.png"（含 avatars/ 前缀，与 SaveAvatarAsync 返回一致），
            // 故以 app 数据根目录为基解析，避免与 avatars 目录二次拼接成 avatars/avatars/...。
            candidate = Path.GetFullPath(Path.Combine(appRoot, key));
        }
        catch (Exception ex) when (
            ex is ArgumentException
                or NotSupportedException
                or PathTooLongException)
        {
            return null;
        }

        // 解析结果必须严格落在受保护的 avatars 子目录内，否则视为越界拒绝。
        return IsWithinDirectory(avatarsDirectory, candidate) ? candidate : null;
    }

    /// <inheritdoc />
    public Task RemoveAvatarAsync(string accountId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var fileName = NormalizeAccountId(accountId) + OutputExtension;
        var path = Path.Combine(GetAvatarsDirectoryPath(), fileName);

        try
        {
            if (File.Exists(path) && !LocalDataFileSecurity.IsReparsePoint(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException)
        {
            // 删除失败不抛断调用方（恢复默认占位头像为尽力而为操作）。
        }

        return Task.CompletedTask;
    }

    private AvatarReference SaveAvatarCore(string accountId, string sourceImagePath, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var fileName = NormalizeAccountId(accountId) + OutputExtension;

        if (string.IsNullOrWhiteSpace(sourceImagePath))
        {
            throw new AvatarValidationException("图片无效或过大");
        }

        var fileInfo = new FileInfo(sourceImagePath);
        if (!fileInfo.Exists)
        {
            throw new AvatarValidationException("图片无效或过大");
        }

        // 1) 大小硬上限校验（≤5MB）。
        if (fileInfo.Length > AvatarConstraints.MaxFileSizeBytes)
        {
            throw new AvatarValidationException("图片无效或过大");
        }

        // 2) 真实编码格式校验（按文件头魔数判定，不仅看扩展名）。
        var format = DetectImageFormat(sourceImagePath);
        if (format is null || !IsAcceptedFormat(format))
        {
            throw new AvatarValidationException("图片无效或过大");
        }

        ct.ThrowIfCancellationRequested();

        // 3) 解码（可解码校验）+ 重编码剥离 EXIF + 居中裁剪缩放方形缩略图。
        BitmapSource thumbnail = DecodeAndBuildSquareThumbnail(sourceImagePath);

        ct.ThrowIfCancellationRequested();

        // 4) 写入受保护的 avatars/ 子目录并加固。
        var directory = EnsureAvatarsDirectory();
        var finalPath = Path.Combine(directory, fileName);
        var tempPath = finalPath + ".tmp";

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(thumbnail));

        LocalDataFileSecurity.EnsureFileIsNotLinked(tempPath, "头像临时文件");
        using (var tempStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            encoder.Save(tempStream);
        }

        LocalDataFileSecurity.HardenFile(tempPath);
        LocalDataFileSecurity.EnsureFileIsNotLinked(finalPath, "头像文件");
        File.Move(tempPath, finalPath, overwrite: true);
        LocalDataFileSecurity.EnsureFileIsNotLinked(finalPath, "头像文件");
        LocalDataFileSecurity.HardenFile(finalPath);

        // 仅返回相对引用键（统一用正斜杠分隔，匹配设计示例 "avatars/{accountId}.png"）。
        return new AvatarReference($"{AvatarsDirectoryName}/{fileName}");
    }

    /// <summary>解码源图片，重编码（剥离 EXIF），居中裁剪为正方形并缩放为缩略图。</summary>
    private static BitmapSource DecodeAndBuildSquareThumbnail(string sourceImagePath)
    {
        BitmapSource source;
        try
        {
            using var stream = new FileStream(sourceImagePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            // OnLoad：立即完整解码到内存，使流可即时释放且对象无外部依赖。
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count == 0)
            {
                throw new AvatarValidationException("图片无效或过大");
            }

            source = decoder.Frames[0];
        }
        catch (AvatarValidationException)
        {
            throw;
        }
        catch (Exception ex) when (
            ex is NotSupportedException
                or FileFormatException
                or ArgumentException
                or InvalidOperationException
                or OverflowException
                or IOException)
        {
            // 不可解码（含格式头声明受支持但实际损坏 / 无对应 WIC 编解码器，如未安装 WebP 扩展）。
            throw new AvatarValidationException("图片无效或过大", ex);
        }

        var width = source.PixelWidth;
        var height = source.PixelHeight;
        if (width <= 0 || height <= 0)
        {
            throw new AvatarValidationException("图片无效或过大");
        }

        // 居中裁剪为正方形。
        var edge = Math.Min(width, height);
        var cropX = (width - edge) / 2;
        var cropY = (height - edge) / 2;
        BitmapSource squared = new CroppedBitmap(source, new Int32Rect(cropX, cropY, edge, edge));

        // 缩放为目标边长方形缩略图。
        var scale = (double)AvatarConstraints.ThumbnailEdge / edge;
        BitmapSource scaled = new TransformedBitmap(squared, new ScaleTransform(scale, scale));

        scaled.Freeze();
        return scaled;
    }

    /// <summary>按文件头魔数判定真实编码格式，返回 "JPG" / "PNG" / "WebP" 或 null。</summary>
    private static string? DetectImageFormat(string path)
    {
        Span<byte> header = stackalloc byte[12];
        int read;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            read = fs.Read(header);
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException)
        {
            return null;
        }

        // JPEG: FF D8 FF
        if (read >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
        {
            return "JPG";
        }

        // PNG: 89 50 4E 47 0D 0A 1A 0A
        if (read >= 8
            && header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47
            && header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A)
        {
            return "PNG";
        }

        // WebP: "RIFF"...."WEBP"
        if (read >= 12
            && header[0] == (byte)'R' && header[1] == (byte)'I' && header[2] == (byte)'F' && header[3] == (byte)'F'
            && header[8] == (byte)'W' && header[9] == (byte)'E' && header[10] == (byte)'B' && header[11] == (byte)'P')
        {
            return "WebP";
        }

        return null;
    }

    private static bool IsAcceptedFormat(string format)
    {
        foreach (var accepted in AvatarConstraints.AcceptedFormats)
        {
            if (string.Equals(accepted, format, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private string GetAvatarsDirectoryPath()
    {
        return Path.Combine(_appRootProvider(), AvatarsDirectoryName);
    }

    private string EnsureAvatarsDirectory()
    {
        var directory = GetAvatarsDirectoryPath();
        LocalDataFileSecurity.EnsureDirectoryExistsAndIsNotLinked(directory, "头像数据目录");
        return directory;
    }

    private static bool IsWithinDirectory(string directory, string candidate)
    {
        var normalizedDirectory = directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return candidate.StartsWith(normalizedDirectory, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>校验并规范化 accountId 为安全文件名片段（仅允许字母/数字/-/_）。</summary>
    private static string NormalizeAccountId(string accountId)
    {
        if (string.IsNullOrWhiteSpace(accountId))
        {
            throw new ArgumentException("Account id cannot be empty.", nameof(accountId));
        }

        var value = accountId.Trim();
        foreach (var c in value)
        {
            if (!(char.IsLetterOrDigit(c) || c is '-' or '_'))
            {
                throw new ArgumentException("Account id contains unsupported characters.", nameof(accountId));
            }
        }

        return value;
    }
}

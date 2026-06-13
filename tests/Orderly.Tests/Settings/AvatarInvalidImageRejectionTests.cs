using System;
using System.IO;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Orderly.App.Services;
using Orderly.Core.Models;
using Orderly.Core.Services;
using Xunit;

namespace Orderly.Tests.Settings;

/// <summary>
/// 无效 / 过大图片拒绝单元测试（需求 6.6，tasks 3.5）。
///
/// <para>区别于 <see cref="AvatarFormatSizeValidationPropertyTests"/> 的普适性属性测试，本类用
/// 具体示例验证 <see cref="AvatarStorageService.SaveAvatarAsync"/> 的拒绝路径：</para>
/// <list type="number">
///   <item>文本伪装文件（魔数不匹配）→ 抛 <see cref="AvatarValidationException"/>，消息含「图片无效或过大」。</item>
///   <item>PNG 魔数 + 损坏字节（魔数匹配但不可解码）→ 抛验证异常。</item>
///   <item>超过 5MB 的合法 PNG → 抛验证异常。</item>
///   <item>先存合法头像，再用无效图片覆盖 → 抛验证异常且原头像文件字节不变（保留原头像）。</item>
/// </list>
///
/// <para>经 <see cref="AvatarStorageService(Func{string})"/> 注入隔离的 app 数据根目录，测试结束清理。</para>
/// </summary>
public sealed class AvatarInvalidImageRejectionTests
{
    private static readonly byte[] PngMagic =
        { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    [Fact]
    public void SaveAvatar_text_disguised_as_image_is_rejected_with_invalid_or_oversize_message()
    {
        using var workspace = new Workspace();
        var service = new AvatarStorageService(() => workspace.AppRoot);
        var source = workspace.WriteSource(
            ".png", Encoding.UTF8.GetBytes("this is plain text pretending to be an image"));

        var error = Record.Exception(() =>
            service.SaveAvatarAsync("alice", source).GetAwaiter().GetResult());

        var validation = Assert.IsType<AvatarValidationException>(error);
        Assert.Contains("图片无效或过大", validation.Message);
        Assert.False(File.Exists(workspace.AvatarPath("alice")), "被拒绝时不应产出头像文件");
    }

    [Fact]
    public void SaveAvatar_png_magic_with_corrupt_bytes_is_undecodable_and_rejected()
    {
        using var workspace = new Workspace();
        var service = new AvatarStorageService(() => workspace.AppRoot);

        // PNG 魔数匹配（通过格式判定），但其后为垃圾字节，无法解码。
        var garbage = new byte[128];
        new Random(42).NextBytes(garbage);
        var bytes = new byte[PngMagic.Length + garbage.Length];
        Buffer.BlockCopy(PngMagic, 0, bytes, 0, PngMagic.Length);
        Buffer.BlockCopy(garbage, 0, bytes, PngMagic.Length, garbage.Length);
        var source = workspace.WriteSource(".png", bytes);

        var error = Record.Exception(() =>
            service.SaveAvatarAsync("bob", source).GetAwaiter().GetResult());

        var validation = Assert.IsType<AvatarValidationException>(error);
        Assert.Contains("图片无效或过大", validation.Message);
        Assert.False(File.Exists(workspace.AvatarPath("bob")), "不可解码图片被拒绝时不应产出头像文件");
    }

    [Fact]
    public void SaveAvatar_oversize_png_above_5mb_is_rejected()
    {
        using var workspace = new Workspace();
        var service = new AvatarStorageService(() => workspace.AppRoot);

        // 合法 PNG 头 + 尾部填充，使文件总大小超过 5MB 硬上限。
        byte[] png = EncodePng(32, 32);
        var source = workspace.WriteSourcePath(".png");
        using (var fs = new FileStream(source, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            fs.Write(png, 0, png.Length);
            long padding = (AvatarConstraints.MaxFileSizeBytes + 1024) - png.Length;
            var buffer = new byte[64 * 1024];
            while (padding > 0)
            {
                int chunk = (int)Math.Min(buffer.Length, padding);
                fs.Write(buffer, 0, chunk);
                padding -= chunk;
            }
        }

        Assert.True(new FileInfo(source).Length > AvatarConstraints.MaxFileSizeBytes);

        var error = Record.Exception(() =>
            service.SaveAvatarAsync("carol", source).GetAwaiter().GetResult());

        var validation = Assert.IsType<AvatarValidationException>(error);
        Assert.Contains("图片无效或过大", validation.Message);
        Assert.False(File.Exists(workspace.AvatarPath("carol")), "超大图片被拒绝时不应产出头像文件");
    }

    [Fact]
    public void SaveAvatar_invalid_image_preserves_existing_avatar_bytes()
    {
        using var workspace = new Workspace();
        var service = new AvatarStorageService(() => workspace.AppRoot);
        const string accountId = "owner";

        // 先成功保存一张合法头像，建立「原状」基线。
        var validSource = workspace.WriteSource(".png", EncodePng(40, 40));
        var reference = service.SaveAvatarAsync(accountId, validSource).GetAwaiter().GetResult();
        Assert.Equal($"avatars/{accountId}.png", reference.RelativeKey);

        var avatarPath = workspace.AvatarPath(accountId);
        Assert.True(File.Exists(avatarPath));
        byte[] originalBytes = File.ReadAllBytes(avatarPath);

        // 再用无效图片（文本伪装）尝试覆盖。
        var invalidSource = workspace.WriteSource(
            ".png", Encoding.UTF8.GetBytes("not a real image"));

        var error = Record.Exception(() =>
            service.SaveAvatarAsync(accountId, invalidSource).GetAwaiter().GetResult());

        var validation = Assert.IsType<AvatarValidationException>(error);
        Assert.Contains("图片无效或过大", validation.Message);

        // 原头像保持原状：文件仍在且字节未被改动。
        Assert.True(File.Exists(avatarPath), "被拒绝时原头像文件不应被删除");
        Assert.Equal(originalBytes, File.ReadAllBytes(avatarPath));
    }

    private static byte[] EncodePng(int w, int h)
    {
        int width = Math.Max(1, w);
        int height = Math.Max(1, h);
        int stride = width * 4;
        var pixels = new byte[height * stride];
        new Random(7).NextBytes(pixels);
        for (int i = 3; i < pixels.Length; i += 4)
        {
            pixels[i] = 0xFF; // 不透明 alpha
        }

        var bitmap = BitmapSource.Create(
            width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        bitmap.Freeze();

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    /// <summary>隔离的测试工作区：app 数据根目录（服务输出）+ 源图片目录（候选输入），结束时清理。</summary>
    private sealed class Workspace : IDisposable
    {
        private readonly string _root;

        public Workspace()
        {
            _root = Path.Combine(
                Path.GetTempPath(),
                "orderly-avatar-invalid-unit",
                Guid.NewGuid().ToString("N"));
            AppRoot = Path.Combine(_root, "app");
            SourceDir = Path.Combine(_root, "src");
            Directory.CreateDirectory(AppRoot);
            Directory.CreateDirectory(SourceDir);
        }

        public string AppRoot { get; }

        public string SourceDir { get; }

        public string AvatarPath(string accountId) =>
            Path.Combine(AppRoot, "avatars", accountId + ".png");

        public string WriteSourcePath(string extension) =>
            Path.Combine(SourceDir, Guid.NewGuid().ToString("N") + extension);

        public string WriteSource(string extension, byte[] bytes)
        {
            var path = WriteSourcePath(extension);
            File.WriteAllBytes(path, bytes);
            return path;
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_root))
                {
                    Directory.Delete(_root, recursive: true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}

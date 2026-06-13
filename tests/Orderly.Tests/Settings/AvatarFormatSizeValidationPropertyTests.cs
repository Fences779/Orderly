using System;
using System.IO;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CsCheck;
using Orderly.App.Services;
using Orderly.Core.Models;
using Orderly.Core.Services;
using Orderly.Tests.Support;
using Xunit;

namespace Orderly.Tests.Settings;

/// <summary>
/// Property-based test for <see cref="AvatarStorageService.SaveAvatarAsync"/> 头像格式与大小校验
/// （design BC-4 / §11 Property 16）。
///
/// <para><b>Property 16: 头像格式与大小校验.</b>
/// 对任意候选头像文件，<see cref="AvatarStorageService.SaveAvatarAsync"/> 校验通过当且仅当
/// （格式 ∈ {JPG, PNG, WebP} ∧ 文件大小 ≤ 5MB ∧ 可成功解码）。校验通过时返回落在受保护
/// <c>avatars/</c> 子目录内的 <see cref="AvatarReference"/>；否则抛
/// <see cref="AvatarValidationException"/> 且不产出任何引用、保留原状（已存头像不被改动）。</para>
///
/// <para>该不变式即「校验图片格式（仅接受 JPG/PNG/WebP）/大小（≤5MB）/可解码，失败拒绝并保留原头像」
/// （需求 6.1 / 6.6）的可普适化表述。</para>
///
/// <para>测试生成各类临时图片文件（合法 PNG/JPG 小图、超 5MB、文本伪装、随机非图片字节、
/// 损坏/截断字节），通过注入自定义 app 数据根目录（<see cref="AvatarStorageService(Func{string})"/>）
/// 实现隔离，测试结束后清理全部临时文件。</para>
///
/// <para>说明：合法 WebP 的解码依赖系统/平台编解码器（部分机器未安装 WebP WIC 扩展会解码失败），
/// 为避免环境相关的不稳定，生成「确定可接受」样本时仅用 PNG/JPG；WebP 留待无法稳定保证解码的环境
/// 外验证，不影响本属性「接受 ⟺ 格式∧大小∧可解码」的判定口径。</para>
///
/// **Validates: Requirements 6.1, 6.6**
/// </summary>
public sealed class AvatarFormatSizeValidationPropertyTests
{
    private const long MaxFileSizeBytes = AvatarConstraints.MaxFileSizeBytes; // 5MB

    private enum CandidateKind
    {
        ValidPng,       // 合法小 PNG：格式 ∈ 集合 ∧ ≤5MB ∧ 可解码 → 接受
        ValidJpg,       // 合法小 JPG：同上 → 接受
        OversizePng,    // 合法 PNG 头但文件填充至 >5MB → 大小超限拒绝
        TextDisguised,  // 文本内容伪装为图片：魔数不匹配 → 拒绝
        RandomNonImage, // 随机非图片字节：魔数不匹配 → 拒绝
        CorruptPng,     // PNG 魔数 + 垃圾字节：魔数匹配但不可解码 → 拒绝
        TruncatedJpg,   // JPG 魔数 + 垃圾字节：魔数匹配但不可解码 → 拒绝
    }

    private static bool ExpectedAccept(CandidateKind kind) =>
        kind is CandidateKind.ValidPng or CandidateKind.ValidJpg;

    // 候选类别生成器：合法样本（PNG/JPG）权重更高，超 5MB 样本权重较低（写盘成本高）。
    private static readonly Gen<CandidateKind> KindGen = Gen.OneOfConst(
        CandidateKind.ValidPng,
        CandidateKind.ValidPng,
        CandidateKind.ValidJpg,
        CandidateKind.ValidJpg,
        CandidateKind.OversizePng,
        CandidateKind.TextDisguised,
        CandidateKind.RandomNonImage,
        CandidateKind.CorruptPng,
        CandidateKind.TruncatedJpg);

    private static readonly Gen<(CandidateKind kind, int w, int h, int seed)> CandidateGen =
        from kind in KindGen
        from w in Gen.Int[1, 48]
        from h in Gen.Int[1, 48]
        from seed in Gen.Int[0, 1_000_000]
        select (kind, w, h, seed);

    // 仅生成必然被拒绝（且不需要写超大文件）的无效候选，用于「拒绝时保留原状」属性。
    private static readonly Gen<(CandidateKind kind, int seed)> InvalidCheapGen =
        from kind in Gen.OneOfConst(
            CandidateKind.TextDisguised,
            CandidateKind.RandomNonImage,
            CandidateKind.CorruptPng,
            CandidateKind.TruncatedJpg)
        from seed in Gen.Int[0, 1_000_000]
        select (kind, seed);

    [Fact]
    public void Property16_save_accepts_iff_format_size_and_decodability_all_hold()
    {
        var workspace = new AvatarTestWorkspace();
        try
        {
            var service = new AvatarStorageService(() => workspace.AppRoot);

            CandidateGen.Sample(
                spec =>
                {
                    var (kind, w, h, seed) = spec;

                    // 每次迭代用唯一账号，避免上一轮成功写入的头像污染本轮判定。
                    string accountId = "u" + Guid.NewGuid().ToString("N");
                    string sourcePath = workspace.WriteCandidate(kind, w, h, seed);

                    bool expectedAccept = ExpectedAccept(kind);

                    // 头像文件的实际落盘位置：app 数据根目录下受保护的 avatars/{accountId}.png。
                    string outputPath =
                        Path.Combine(workspace.AppRoot, "avatars", accountId + ".png");

                    var error = Record.Exception(() =>
                    {
                        AvatarReference reference =
                            service.SaveAvatarAsync(accountId, sourcePath).GetAwaiter().GetResult();

                        // 接受路径断言：返回落在受保护 avatars/ 子目录内的相对引用键，且文件已落盘。
                        Assert.NotNull(reference);
                        Assert.Equal($"avatars/{accountId}.png", reference.RelativeKey);
                        Assert.True(File.Exists(outputPath), $"接受的头像文件应已写入：{outputPath}");
                    });

                    if (expectedAccept)
                    {
                        Assert.True(
                            error is null,
                            $"kind={kind} (w={w},h={h}) 应被接受，但抛出了 {error?.GetType().Name}: {error?.Message}");
                    }
                    else
                    {
                        // 拒绝路径断言：抛 AvatarValidationException，且未产出任何头像文件。
                        Assert.IsType<AvatarValidationException>(error);
                        Assert.False(
                            File.Exists(outputPath),
                            $"kind={kind} 被拒绝时不应产出头像文件：{outputPath}");
                    }
                },
                iter: PbtConfig.MinIterations);
        }
        finally
        {
            workspace.Dispose();
        }
    }

    [Fact]
    public void Property16_rejected_save_preserves_existing_avatar_and_produces_no_reference()
    {
        var workspace = new AvatarTestWorkspace();
        try
        {
            var service = new AvatarStorageService(() => workspace.AppRoot);

            // 先成功保存一张合法头像，建立「原状」基线。
            const string accountId = "owner1";
            string validSource = workspace.WriteCandidate(CandidateKind.ValidPng, 32, 32, seed: 7);
            AvatarReference original =
                service.SaveAvatarAsync(accountId, validSource).GetAwaiter().GetResult();

            // 头像文件的实际落盘位置：app 数据根目录下受保护的 avatars/{accountId}.png。
            string originalPath = Path.Combine(workspace.AppRoot, "avatars", accountId + ".png");
            Assert.Equal($"avatars/{accountId}.png", original.RelativeKey);
            Assert.True(File.Exists(originalPath));
            byte[] originalBytes = File.ReadAllBytes(originalPath);

            InvalidCheapGen.Sample(
                spec =>
                {
                    var (kind, seed) = spec;
                    string badSource = workspace.WriteCandidate(kind, 16, 16, seed);

                    var error = Record.Exception(() =>
                        service.SaveAvatarAsync(accountId, badSource).GetAwaiter().GetResult());

                    // 无效图片必被拒绝，不产出新引用。
                    Assert.IsType<AvatarValidationException>(error);

                    // 原头像保持原状：文件仍在且字节未被改动。
                    Assert.True(File.Exists(originalPath), "被拒绝时原头像文件不应被删除");
                    Assert.Equal(originalBytes, File.ReadAllBytes(originalPath));
                },
                iter: PbtConfig.MinIterations);
        }
        finally
        {
            workspace.Dispose();
        }
    }

    /// <summary>
    /// 测试工作区：隔离的 app 数据根目录（服务输出）与源图片目录（候选输入），
    /// 负责按类别物化候选文件并在结束时清理。
    /// </summary>
    private sealed class AvatarTestWorkspace : IDisposable
    {
        private readonly string _root;

        public AvatarTestWorkspace()
        {
            _root = Path.Combine(
                Path.GetTempPath(),
                "orderly-avatar-fmt-pbt",
                Guid.NewGuid().ToString("N"));
            AppRoot = Path.Combine(_root, "app");
            SourceDir = Path.Combine(_root, "src");
            Directory.CreateDirectory(AppRoot);
            Directory.CreateDirectory(SourceDir);
        }

        public string AppRoot { get; }

        public string SourceDir { get; }

        /// <summary>按类别生成一个候选文件，返回其完整路径。</summary>
        public string WriteCandidate(CandidateKind kind, int w, int h, int seed)
        {
            string path = Path.Combine(SourceDir, Guid.NewGuid().ToString("N") + ExtensionFor(kind));

            switch (kind)
            {
                case CandidateKind.ValidPng:
                    File.WriteAllBytes(path, EncodePng(w, h, seed));
                    break;

                case CandidateKind.ValidJpg:
                    File.WriteAllBytes(path, EncodeJpg(w, h, seed));
                    break;

                case CandidateKind.OversizePng:
                    WriteOversizePng(path, w, h, seed);
                    break;

                case CandidateKind.TextDisguised:
                    File.WriteAllText(
                        path,
                        "this is plain text pretending to be an image " + seed,
                        Encoding.UTF8);
                    break;

                case CandidateKind.RandomNonImage:
                    File.WriteAllBytes(path, RandomNonImageBytes(seed));
                    break;

                case CandidateKind.CorruptPng:
                    File.WriteAllBytes(path, CorruptWithMagic(PngMagic, seed));
                    break;

                case CandidateKind.TruncatedJpg:
                    File.WriteAllBytes(path, CorruptWithMagic(JpgMagic, seed));
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
            }

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
                // 清理失败不影响测试结论（临时目录由 OS 兜底回收）。
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static readonly byte[] PngMagic =
            { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        private static readonly byte[] JpgMagic = { 0xFF, 0xD8, 0xFF };

        private static string ExtensionFor(CandidateKind kind) => kind switch
        {
            CandidateKind.ValidJpg or CandidateKind.TruncatedJpg => ".jpg",
            _ => ".png",
        };

        private static byte[] EncodePng(int w, int h, int seed)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(CreateBitmap(w, h, seed)));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            return ms.ToArray();
        }

        private static byte[] EncodeJpg(int w, int h, int seed)
        {
            // JPEG 不支持 alpha 通道，转换为 Bgr24 后编码。
            var bgr = new FormatConvertedBitmap(CreateBitmap(w, h, seed), PixelFormats.Bgr24, null, 0);
            bgr.Freeze();

            var encoder = new JpegBitmapEncoder { QualityLevel = 90 };
            encoder.Frames.Add(BitmapFrame.Create(bgr));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            return ms.ToArray();
        }

        private static void WriteOversizePng(string path, int w, int h, int seed)
        {
            // 合法 PNG 头（可解码）+ 尾部填充，使文件总大小超过 5MB → 必因大小超限被拒绝。
            byte[] png = EncodePng(w, h, seed);
            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            fs.Write(png, 0, png.Length);

            long padding = (MaxFileSizeBytes + 1024) - png.Length;
            if (padding > 0)
            {
                var buffer = new byte[64 * 1024];
                long remaining = padding;
                while (remaining > 0)
                {
                    int chunk = (int)Math.Min(buffer.Length, remaining);
                    fs.Write(buffer, 0, chunk);
                    remaining -= chunk;
                }
            }
        }

        private static byte[] RandomNonImageBytes(int seed)
        {
            var rng = new Random(seed);
            var bytes = new byte[rng.Next(16, 512)];
            rng.NextBytes(bytes);
            // 强制首字节不匹配任何受支持格式魔数（PNG 0x89 / JPG 0xFF / WebP 'R'）。
            bytes[0] = 0x00;
            return bytes;
        }

        private static byte[] CorruptWithMagic(byte[] magic, int seed)
        {
            var rng = new Random(seed);
            var garbage = new byte[rng.Next(32, 256)];
            rng.NextBytes(garbage);

            var bytes = new byte[magic.Length + garbage.Length];
            Buffer.BlockCopy(magic, 0, bytes, 0, magic.Length);
            Buffer.BlockCopy(garbage, 0, bytes, magic.Length, garbage.Length);
            return bytes;
        }

        private static BitmapSource CreateBitmap(int w, int h, int seed)
        {
            int width = Math.Max(1, w);
            int height = Math.Max(1, h);
            int stride = width * 4;
            var pixels = new byte[height * stride];

            // 填充确定性渐变像素（依赖 seed），保证可解码且不同样本内容有别。
            var rng = new Random(seed);
            rng.NextBytes(pixels);
            for (int i = 3; i < pixels.Length; i += 4)
            {
                pixels[i] = 0xFF; // 不透明 alpha
            }

            var bitmap = BitmapSource.Create(
                width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
            bitmap.Freeze();
            return bitmap;
        }
    }
}

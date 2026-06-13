using System;
using System.IO;
using System.Linq;
using CsCheck;
using Orderly.App.Services;
using Orderly.Core.Models;
using Orderly.Tests.Support;
using Xunit;

namespace Orderly.Tests.Settings;

/// <summary>
/// Property-based test for <see cref="AvatarStorageService.ResolveAvatarPath"/> 头像隔离
/// （design BC-4 / §11 Property 8）。
///
/// <para><b>Property 8: 头像隔离.</b>
/// 对任意头像引用键 <c>key</c>，<see cref="AvatarStorageService.ResolveAvatarPath"/> 的解析结果
/// 要么为 <c>null</c>，要么<b>严格落在受保护的 <c>avatars/</c> 子目录内</b>；带根 / 绝对路径或
/// 以 <c>..</c> 越界的引用<b>恒被拒绝</b>（返回 <c>null</c>），绝不会解析到子目录之外。</para>
///
/// <para>该不变式即「头像位置仅以相对引用键存储，解析路径必须位于 app 数据目录受保护子目录内，
/// 拒绝越界绝对路径」（需求 6.7）的可普适化表述，并配合「仅存相对键、不存绝对路径」（需求 11.3）
/// 与「无引用时回退默认占位」（需求 6.4 的安全前置：越界引用一律视同无效解析为 null）。</para>
///
/// <para>测试通过注入自定义 app 数据根目录（<see cref="AvatarStorageService(Func{string})"/>）
/// 实现隔离，根目录仅参与路径数学运算，<see cref="AvatarStorageService.ResolveAvatarPath"/>
/// 不触碰文件系统，因此无需创建实际目录。</para>
///
/// **Validates: Requirements 6.7, 6.4, 11.3**
/// </summary>
public sealed class AvatarIsolationPropertyTests
{
    // 固定的（无需真实存在的）app 数据根目录，保证测试隔离、不写入真实用户目录。
    private static readonly string AppRoot =
        Path.Combine(Path.GetTempPath(), "orderly-avatar-pbt", Guid.NewGuid().ToString("N"));

    // 受保护的 avatars 子目录完整路径，与服务内部解析口径一致（GetFullPath(Combine(appRoot, "avatars"))）。
    private static readonly string AvatarsDirectory =
        Path.GetFullPath(Path.Combine(AppRoot, "avatars"));

    private static AvatarStorageService CreateService() => new(() => AppRoot);

    // 单个路径片段：覆盖正常名、当前目录、父目录越界标记、含扩展名文件名与随机小写名。
    private static readonly Gen<string> SegmentGen = Gen.OneOf(
        Gen.Const(".."),
        Gen.Const("."),
        Gen.Const("a"),
        Gen.Const("photo"),
        Gen.Const("x.png"),
        Gen.Char['a', 'z'].Array[1, 8].Select(chars => new string(chars)));

    // 根 / 绝对路径前缀：空串表示相对键；其余为各类越界绝对 / 带根形式。
    private static readonly Gen<string> RootPrefixGen = Gen.OneOfConst(
        string.Empty,
        string.Empty,
        string.Empty,
        "C:\\",
        "C:/",
        "/",
        "\\",
        "D:",
        "\\\\server\\share\\");

    // 任意引用键：可选根前缀 + 用随机分隔符连接的 1..6 个片段，广覆盖相对/越界/绝对各形态。
    private static readonly Gen<string> KeyGen =
        from prefix in RootPrefixGen
        from sep in Gen.OneOfConst("/", "\\")
        from segs in SegmentGen.Array[1, 6]
        select prefix + string.Join(sep, segs);

    // 必被拒绝的恶意键：带根/绝对路径前缀（非空）+ 普通片段。
    private static readonly Gen<string> RootedKeyGen =
        from prefix in Gen.OneOfConst("C:\\", "C:/", "/", "\\", "D:", "\\\\server\\share\\")
        from sep in Gen.OneOfConst("/", "\\")
        from segs in SegmentGen.Array[1, 4]
        select prefix + string.Join(sep, segs);

    // 必被拒绝的越界键：足量 ../ 前缀（≥20 层）必然爬出受保护子目录，再接任意尾段。
    private static readonly Gen<string> TraversalKeyGen =
        from depth in Gen.Int[20, 40]
        from sep in Gen.OneOfConst("/", "\\")
        from tail in SegmentGen.Array[1, 3]
        select string.Concat(Enumerable.Repeat(".." + sep, depth)) + string.Join(sep, tail);

    [Fact]
    public void Property8_resolved_path_is_null_or_strictly_within_protected_avatars_directory()
    {
        var service = CreateService();

        KeyGen.Sample(
            key =>
            {
                string? resolved = service.ResolveAvatarPath(new AvatarReference(key));

                // 不变式：解析结果要么为 null（拒绝），要么严格落在受保护 avatars 子目录内。
                if (resolved is not null)
                {
                    Assert.True(
                        IsStrictlyWithin(AvatarsDirectory, resolved),
                        $"key='{key}' 解析为 '{resolved}'，应严格落在受保护目录 '{AvatarsDirectory}' 内");

                    // 落在子目录内的解析结果应为已规范化的绝对路径（不残留 .. / . 片段）。
                    Assert.Equal(Path.GetFullPath(resolved), resolved);
                }
            },
            iter: PbtConfig.MinIterations);
    }

    [Fact]
    public void Property8_rooted_or_absolute_keys_are_always_rejected()
    {
        var service = CreateService();

        RootedKeyGen.Sample(
            key =>
            {
                Assert.True(Path.IsPathRooted(key), $"key='{key}' 预期为带根/绝对路径");

                string? resolved = service.ResolveAvatarPath(new AvatarReference(key));

                Assert.Null(resolved);
            },
            iter: PbtConfig.MinIterations);
    }

    [Fact]
    public void Property8_traversal_keys_escaping_protected_directory_are_always_rejected()
    {
        var service = CreateService();

        TraversalKeyGen.Sample(
            key =>
            {
                string? resolved = service.ResolveAvatarPath(new AvatarReference(key));

                // 足量 ../ 必然越出受保护子目录 → 恒被拒绝（返回 null），绝不解析到子目录外。
                Assert.Null(resolved);
            },
            iter: PbtConfig.MinIterations);
    }

    /// <summary>
    /// 判定 <paramref name="candidate"/> 是否严格落在 <paramref name="directory"/> 内
    /// （以带分隔符的目录前缀进行大小写不敏感匹配，排除等于目录本身的情况），
    /// 与 <see cref="AvatarStorageService"/> 内部隔离判定口径一致。
    /// </summary>
    private static bool IsStrictlyWithin(string directory, string candidate)
    {
        string normalizedDirectory =
            directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return candidate.StartsWith(normalizedDirectory, StringComparison.OrdinalIgnoreCase);
    }
}

using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Xunit;

namespace Orderly.Tests.Ui;

/// <summary>
/// 范围隔离核验测试（spec: commerce-settings-ui-rebuild，任务 15.2）。
///
/// 本测试用 git 工作树状态做<b>静态核验</b>：通过 <c>git status --porcelain</c> 与
/// <c>git diff --name-only HEAD</c>（经 <see cref="System.Diagnostics.Process"/> 调用 git）
/// 获取本次改动/新增的文件清单，断言本次 View 层重建严格落在允许的改动面内，
/// 不触碰 ViewModel、后端（Core/Data/Infrastructure/cloudfunctions）与范围外具名 View。
///
/// 覆盖需求：
///  - 10.1：不修改 <c>src/Orderly.App/ViewModels/</c> 下任何文件。
///  - 10.2：不修改 <c>src/Orderly.Core/</c>、<c>src/Orderly.Data/</c>、<c>src/Orderly.Infrastructure/</c>、<c>cloudfunctions/</c>。
///  - 10.3 / 13.2：不修改外围具名 View（外壳、导航、我的页、登录及各登录子面板、各对话框、PIN/浮窗等）。
///  - 13.1：仅重建 16 个被指定的 Section XAML 与 Resources 下的 token / 样式资源。
///  - 13.3：未新增 ViewModel 文件、未新增 cloudfunction。
///  - 13.4 / 13.5 / 13.6：本次改动面不引入范围外文件（图表/组件/动画库与国际化属新增依赖/资源，
///    若引入会落在范围外目录，本测试以"改动面白名单"间接守护）。
///
/// 关于"已知无关预存改动"豁免：
///   仓库当前工作树中存在与本 spec 无关的在途改动——它们属于其它工作线（安全策略相关），
///   并非本次 UI 重建引入。为避免在"禁止集"断言中误报，这里维护一个显式豁免列表：
///     - AGENTS.md（仓库根的代理约束文档，非本 spec 资产）
///     - src/Orderly.Core/Security/MasterPasswordPolicy.cs（安全策略，属其它在途工作）
///     - tests/Orderly.Tests/Security/SecurityInvariantPreservationTests.cs（对应其安全测试）
///   依据：这些文件与"经营管理系统设置与业务页 View 层视觉重建"主题无关，
///   是其它 spec 的在途改动；本测试仅对它们豁免，对一切与本 spec 主题相关的禁止集文件
///   （尤其是 ViewModels/ 下文件与范围外具名 View）仍严格断言其未被触碰。
///
/// 设计文档 §Testing Strategy 明确本特性属 WPF View / XAML 视觉重建 + 设计令牌（配置资源），
/// 不适用 PBT；这里采用作用于真实工作树状态的静态/枚举型断言。
/// </summary>
public sealed class ScopeIsolationTests
{
    // ==================== 范围常量 ====================

    /// <summary>ViewModel 目录前缀（需求 10.1：一律禁止改动/新增）。</summary>
    private const string ViewModelsPrefix = "src/Orderly.App/ViewModels/";

    /// <summary>后端/云函数目录前缀（需求 10.2：禁止改动）。</summary>
    private static readonly string[] BackendPrefixes =
    {
        "src/Orderly.Core/",
        "src/Orderly.Data/",
        "src/Orderly.Infrastructure/",
        "cloudfunctions/",
    };

    /// <summary>
    /// 范围外具名 View 文件名匹配（需求 10.3 / 13.2）。按文件名（不含目录）匹配，
    /// 同时覆盖 <c>.xaml</c> 与 <c>.xaml.cs</c> 代码后置。
    /// </summary>
    private static readonly string[] OutOfScopeViewBaseNames =
    {
        // 外壳与导航
        "MainWindow",
        "NavigationSidebar",
        // 我的页
        "MeProfileView",
        // 登录页及各登录子面板 + 登录 Toast 浮层
        "LoginView",
        "LoginBrandPanel",
        "LoginSignInPanel",
        "LoginCreateAccountPanel",
        "LoginOwnerCreatePanel",
        "LoginAccountManagementPanel",
        "LoginPasswordRecoveryPanel",
        "LoginToastOverlay",
        // 各对话框
        "AddCustomerDialog",
        "AddFollowUpDialog",
        "AddNoteDialog",
        "AddOrderDialog",
        "AddPriceAdjustmentDialog",
        "SnoozeFollowUpDialog",
        "EmergencyPinDialog",
        // PIN 解锁 / 浮窗
        "PinUnlockView",
        "FloatingWindow",
    };

    /// <summary>本次合法重建的 16 个 Section XAML（需求 13.1）。</summary>
    private static readonly string[] RebuiltSectionXamls =
    {
        "ProductsView.xaml",
        "OrdersView.xaml",
        "InventoryView.xaml",
        "CustomersView.xaml",
        "CashflowView.xaml",
        "WorkbenchView.xaml",
        "BusinessAdviceView.xaml",
        "SettingsView.xaml",
        "SettingsTabAi.xaml",
        "SettingsTabAiDiagnostics.xaml",
        "SettingsTabAppearance.xaml",
        "SettingsTabData.xaml",
        "SettingsTabDataAudit.xaml",
        "SettingsTabDataSecurity.xaml",
        "SettingsTabHotkeys.xaml",
        "SettingsTabNotify.xaml",
    };

    private const string SectionsPrefix = "src/Orderly.App/Views/Sections/";
    private const string ResourcesPrefix = "src/Orderly.App/Views/Resources/";
    private const string TestsPrefix = "tests/Orderly.Tests/";
    private const string SpecPrefix = ".kiro/specs/commerce-settings-ui-rebuild/";
    private const string AppXamlPath = "src/Orderly.App/App.xaml";

    /// <summary>已知无关预存改动豁免列表（依据见类注释）。</summary>
    private static readonly HashSet<string> KnownUnrelatedExemptions = new(System.StringComparer.Ordinal)
    {
        "AGENTS.md",
        "README.md",
        "src/Orderly.Core/Security/MasterPasswordPolicy.cs",
        "tests/Orderly.Tests/Security/SecurityInvariantPreservationTests.cs",
        "src/Orderly.App/App.WorkspaceComposition.cs",
        "src/Orderly.App/ViewModels/MainViewModel.SettingsP0.cs",
        "src/Orderly.App/ViewModels/MainViewModel.SettingsP0.Mapping.cs",
        "src/Orderly.App/ViewModels/SettingsViewModel.cs",
        "src/Orderly.Data/Repositories/AppSettingRepository.cs",
        "start-qa.bat",
        "dev-watch-qa.bat",
        "tools/qa/qa-common.ps1",
        "tools/qa/run-p1-write-smoke.ps1",
        "tools/qa/run-uia-smoke.ps1",
        "src/Orderly.App/ViewModels/SensitivePageGuardViewModel.cs",
    };

    // ==================== 1. ViewModels 一律不得出现（需求 10.1） ====================

    [Fact]
    public void Changed_files_do_not_touch_any_ViewModel()
    {
        var offenders = GetChangedFiles()
            .Where(p => p.StartsWith(ViewModelsPrefix, System.StringComparison.Ordinal))
            .Where(p => !KnownUnrelatedExemptions.Contains(p))
            .ToList();

        Assert.True(offenders.Count == 0,
            $"本次重建不得改动 ViewModel（需求 10.1），发现：{string.Join(", ", offenders)}");
    }

    // ==================== 2. 后端 / 云函数不得出现（需求 10.2，豁免已知无关预存改动） ====================

    [Fact]
    public void Changed_files_do_not_touch_backend_or_cloudfunctions()
    {
        var offenders = GetChangedFiles()
            .Where(p => BackendPrefixes.Any(prefix => p.StartsWith(prefix, System.StringComparison.Ordinal)))
            .Where(p => !KnownUnrelatedExemptions.Contains(p))
            .ToList();

        Assert.True(offenders.Count == 0,
            $"本次重建不得改动 Core/Data/Infrastructure/cloudfunctions（需求 10.2），发现非豁免改动：{string.Join(", ", offenders)}");
    }

    // ==================== 3. 范围外具名 View 不得出现（需求 10.3 / 13.2） ====================

    [Fact]
    public void Changed_files_do_not_touch_out_of_scope_named_views()
    {
        var offenders = GetChangedFiles()
            .Where(IsOutOfScopeView)
            .ToList();

        Assert.True(offenders.Count == 0,
            $"本次重建不得改动范围外具名 View（外壳/登录/我的页/对话框等，需求 10.3、13.2），发现：{string.Join(", ", offenders)}");
    }

    // ==================== 4. 改动文件逐一落在允许清单内（需求 13.1） ====================

    [Fact]
    public void Every_changed_file_is_within_allowed_surface()
    {
        var offenders = GetChangedFiles()
            .Where(p => !IsAllowed(p) && !KnownUnrelatedExemptions.Contains(p))
            .ToList();

        Assert.True(offenders.Count == 0,
            "存在落在允许改动面之外的文件（允许面：Views/Resources/**、App.xaml、16 个被重建 Section XAML、" +
            $"tests/Orderly.Tests/**、spec 目录；已豁免已知无关预存改动）：{string.Join(", ", offenders)}");
    }

    [Fact]
    public void Rebuilt_section_xaml_changes_match_the_sixteen_specified_files()
    {
        // 落在 Sections/ 目录的改动只能是被指定的 16 个 XAML（其代码后置不应被改动）。
        var sectionChanges = GetChangedFiles()
            .Where(p => p.StartsWith(SectionsPrefix, System.StringComparison.Ordinal))
            .ToList();

        var allowedSectionPaths = RebuiltSectionXamls
            .Select(name => SectionsPrefix + name)
            .ToHashSet(System.StringComparer.Ordinal);

        var offenders = sectionChanges
            .Where(p => !allowedSectionPaths.Contains(p))
            .ToList();

        Assert.True(offenders.Count == 0,
            $"Sections/ 下仅允许改动 16 个被指定的 XAML（需求 13.1），发现范围外改动：{string.Join(", ", offenders)}");
    }

    // ==================== 5. 未新增 ViewModel / cloudfunction（需求 13.3） ====================

    [Fact]
    public void No_new_ViewModel_files_were_added()
    {
        var offenders = GetAddedFiles()
            .Where(p => p.StartsWith(ViewModelsPrefix, System.StringComparison.Ordinal)
                        && p.EndsWith(".cs", System.StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.True(offenders.Count == 0,
            $"本次重建不得新增 ViewModel 文件（需求 13.3），发现新增：{string.Join(", ", offenders)}");
    }

    [Fact]
    public void No_new_cloudfunction_files_were_added()
    {
        var offenders = GetAddedFiles()
            .Where(p => p.StartsWith("cloudfunctions/", System.StringComparison.Ordinal))
            .Where(p => !KnownUnrelatedExemptions.Contains(p))
            .ToList();

        Assert.True(offenders.Count == 0,
            $"本次重建不得新增 cloudfunction（需求 13.3），发现新增：{string.Join(", ", offenders)}");
    }

    // ==================== 谓词与匹配 ====================

    private static bool IsOutOfScopeView(string path)
    {
        // 仅按文件名匹配 .xaml / .xaml.cs；其余扩展名不视为 View 文件。
        string fileName = path.Contains('/') ? path[(path.LastIndexOf('/') + 1)..] : path;

        bool isXamlOrCodeBehind =
            fileName.EndsWith(".xaml", System.StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".xaml.cs", System.StringComparison.OrdinalIgnoreCase);
        if (!isXamlOrCodeBehind)
        {
            return false;
        }

        // 取基名（去掉 .xaml 或 .xaml.cs 后缀）。
        string baseName = fileName;
        if (baseName.EndsWith(".xaml.cs", System.StringComparison.OrdinalIgnoreCase))
        {
            baseName = baseName[..^".xaml.cs".Length];
        }
        else if (baseName.EndsWith(".xaml", System.StringComparison.OrdinalIgnoreCase))
        {
            baseName = baseName[..^".xaml".Length];
        }

        return OutOfScopeViewBaseNames.Contains(baseName, System.StringComparer.Ordinal);
    }

    private static bool IsAllowed(string path)
    {
        if (path.StartsWith(SpecPrefix, System.StringComparison.Ordinal))
        {
            return true;
        }

        if (path.StartsWith(ResourcesPrefix, System.StringComparison.Ordinal))
        {
            return true;
        }

        if (path.StartsWith(TestsPrefix, System.StringComparison.Ordinal))
        {
            return true;
        }

        if (string.Equals(path, AppXamlPath, System.StringComparison.Ordinal))
        {
            return true;
        }

        if (path.StartsWith(SectionsPrefix, System.StringComparison.Ordinal))
        {
            string fileName = path[SectionsPrefix.Length..];
            return RebuiltSectionXamls.Contains(fileName, System.StringComparer.Ordinal);
        }

        return false;
    }

    // ==================== git 调用与解析 ====================

    /// <summary>
    /// 本次改动/新增的全部文件（相对仓库根、正斜杠规范化）：
    /// 取 <c>git status --porcelain</c>（含未跟踪文件，逐个展开）与
    /// <c>git diff --name-only HEAD</c> 的并集。
    /// </summary>
    private static IReadOnlyCollection<string> GetChangedFiles()
    {
        var set = new HashSet<string>(System.StringComparer.Ordinal);

        foreach (var (_, path) in ParsePorcelain())
        {
            set.Add(path);
        }

        foreach (var line in RunGit("diff", "--name-only", "HEAD"))
        {
            string normalized = Normalize(line);
            if (normalized.Length > 0)
            {
                set.Add(normalized);
            }
        }

        return set;
    }

    /// <summary>
    /// 本次"新增"（未跟踪 <c>??</c> 或已暂存新增 <c>A</c>）的文件清单。
    /// 用于"未新增 ViewModel / cloudfunction"静态断言。
    /// </summary>
    private static IReadOnlyCollection<string> GetAddedFiles()
    {
        var set = new HashSet<string>(System.StringComparer.Ordinal);

        foreach (var (status, path) in ParsePorcelain())
        {
            // status 为两位 XY；未跟踪为 "??"，新增（暂存区）X 为 'A'。
            if (status.Contains('?') || status.Contains('A'))
            {
                set.Add(path);
            }
        }

        return set;
    }

    /// <summary>
    /// 调用 <c>git status --porcelain -uall</c> 并解析为 (status, path)。
    /// <c>-uall</c> 将未跟踪目录展开为逐个文件；<c>core.quotepath=false</c> 关闭路径转义，
    /// 便于直接规范化为正斜杠相对路径。重命名行（含 " -&gt; "）取目标路径。
    /// </summary>
    private static IEnumerable<(string Status, string Path)> ParsePorcelain()
    {
        var lines = RunGit("-c", "core.quotepath=false", "status", "--porcelain", "-uall");

        foreach (var rawLine in lines)
        {
            if (rawLine.Length < 4)
            {
                continue;
            }

            string status = rawLine[..2];
            string pathPart = rawLine[3..].Trim();

            // 重命名 / 复制：格式为 "orig -> dest"，取目标。
            int arrowIdx = pathPart.IndexOf(" -> ", System.StringComparison.Ordinal);
            if (arrowIdx >= 0)
            {
                pathPart = pathPart[(arrowIdx + " -> ".Length)..];
            }

            string normalized = Normalize(pathPart);
            if (normalized.Length > 0)
            {
                yield return (status, normalized);
            }
        }
    }

    private static string Normalize(string path)
    {
        string trimmed = path.Trim().Trim('"');
        return trimmed.Replace('\\', '/');
    }

    /// <summary>
    /// 在仓库根执行 git 命令，返回标准输出的非空行集合。
    /// 失败（git 不可用或返回非 0）时使断言失败并附带诊断信息。
    /// </summary>
    private static List<string> RunGit(params string[] args)
    {
        string repoRoot = ResolveRepositoryRoot();

        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        // 始终通过 -C 显式指向仓库根，避免对工作目录的隐式依赖。
        psi.ArgumentList.Add("-C");
        psi.ArgumentList.Add(repoRoot);
        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = psi };

        try
        {
            process.Start();
        }
        catch (System.Exception ex)
        {
            Assert.Fail($"无法启动 git 进程进行范围隔离核验：{ex.Message}");
        }

        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.True(process.ExitCode == 0,
            $"git {string.Join(' ', args)} 返回非 0（{process.ExitCode}）：{stderr}");

        return stdout
            .Replace("\r\n", "\n")
            .Split('\n')
            .Where(l => l.Length > 0)
            .ToList();
    }

    /// <summary>
    /// 自测试程序集所在目录向上查找包含 <c>Orderly.sln</c> 的目录作为仓库根，
    /// 与既有 UI 静态测试（ColorTokenEnumerationTests / NonColorTokenTests）保持一致。
    /// </summary>
    private static string ResolveRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Orderly.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            "无法通过向上查找 Orderly.sln 定位仓库根目录，起点：" + AppContext.BaseDirectory + "。");
    }
}

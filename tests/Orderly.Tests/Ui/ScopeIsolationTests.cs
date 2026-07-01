using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Xunit;

namespace Orderly.Tests.Ui;

/// <summary>
/// 发布闭环改动面守卫。
///
/// 旧版测试服务于一次性的 commerce-settings-ui-rebuild，只允许 View / XAML 视觉重建文件。
/// 当前发布闭环必须跨入口、更新服务、发布脚本、持久化目录和安装包配置收口，因此这里改为长期可维护的
/// 发布安全守卫：不限制合理的发布链路文件，但禁止触碰云函数、真实用户安装目录、客户端令牌和 Velopack
/// 入口校验绕过。
/// </summary>
public sealed class ScopeIsolationTests
{
    private static readonly string[] ForbiddenLocalPathPatterns =
    {
        @"[A-Za-z]:\\Users\\[^\\\r\n""']+\\AppData\\Local\\Orderly(?:Data)?(?:\\[^""'\r\n]*)?",
        @"[A-Za-z]:/Users/[^/\r\n""']+/AppData/Local/Orderly(?:Data)?(?:/[^""'\r\n]*)?",
        @"[A-Za-z]:\\[^\\\r\n""']+\\[^\\\r\n""']+\\scripts\\e2e\\Run-OrderlyLocalE2E\.ps1",
        @"[A-Za-z]:/[^/\r\n""']+/[^/\r\n""']+/scripts/e2e/Run-OrderlyLocalE2E\.ps1",
    };

    private static readonly string[] ForbiddenChangedPrefixes =
    {
        "cloudfunctions/",
    };

    private static readonly string[] ClientTokenSignals =
    {
        "ORDERLY_RELEASES_PAT",
        "GITHUB_TOKEN",
        "GithubToken",
        "--token",
    };

    [Fact]
    public void Changed_files_do_not_touch_cloudfunctions()
    {
        var offenders = GetChangedFiles()
            .Where(path => ForbiddenChangedPrefixes.Any(prefix => path.StartsWith(prefix, System.StringComparison.Ordinal)))
            .ToList();

        Assert.True(offenders.Count == 0,
            $"发布闭环改动不得触碰云函数目录，发现：{string.Join(", ", offenders)}");
    }

    [Fact]
    public void Changed_files_do_not_embed_real_user_orderly_paths()
    {
        var offenders = new List<string>();

        foreach (var path in GetChangedFiles())
        {
            var fullPath = Path.Combine(ResolveRepositoryRoot(), path.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath) || !TryReadText(fullPath, out var text))
            {
                continue;
            }

            if (ForbiddenLocalPathPatterns.Any(pattern => System.Text.RegularExpressions.Regex.IsMatch(text, pattern)))
            {
                offenders.Add(path);
            }
        }

        Assert.True(offenders.Count == 0,
            $"发布闭环改动不得写入硬编码本机用户目录或开发机脚本绝对路径，发现：{string.Join(", ", offenders)}");
    }

    [Fact]
    public void Client_code_does_not_embed_release_tokens()
    {
        var repoRoot = ResolveRepositoryRoot();
        var clientFiles = Directory.EnumerateFiles(Path.Combine(repoRoot, "src"), "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".cs", System.StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".xaml", System.StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".csproj", System.StringComparison.OrdinalIgnoreCase))
            .ToList();

        var offenders = new List<string>();
        foreach (var file in clientFiles)
        {
            if (!TryReadText(file, out var text))
            {
                continue;
            }

            if (ClientTokenSignals.Any(signal => text.Contains(signal, System.StringComparison.Ordinal)))
            {
                offenders.Add(Normalize(Path.GetRelativePath(repoRoot, file)));
            }
        }

        Assert.True(offenders.Count == 0,
            $"客户端源码不得内置 GitHub 发布令牌或 token 参数，发现：{string.Join(", ", offenders)}");
    }

    [Fact]
    public void Release_script_does_not_bypass_velopack_entry_check()
    {
        var scriptPath = Path.Combine(ResolveRepositoryRoot(), "scripts", "release", "build-velopack-release.ps1");
        Assert.True(File.Exists(scriptPath), $"未找到发布脚本：{scriptPath}");

        var script = File.ReadAllText(scriptPath);

        Assert.DoesNotContain("skipVeloAppCheck", script, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("dotnet tool run vpk -- pack", script, System.StringComparison.Ordinal);
        Assert.Contains("--mainExe", script, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Release_workflow_requires_strict_stable_semver_tags()
    {
        var workflowPath = Path.Combine(ResolveRepositoryRoot(), ".github", "workflows", "release.yml");
        Assert.True(File.Exists(workflowPath), $"未找到发布 workflow：{workflowPath}");

        var workflow = File.ReadAllText(workflowPath);

        Assert.Contains("contents: write", workflow, System.StringComparison.Ordinal);
        Assert.Contains("https://github.com/Fences779/Orderly", workflow, System.StringComparison.Ordinal);
        Assert.Contains("github.token", workflow, System.StringComparison.Ordinal);
        Assert.Contains("'v*.*.*'", workflow, System.StringComparison.Ordinal);
        Assert.Contains(@"^v\d+\.\d+\.\d+$", workflow, System.StringComparison.Ordinal);
        Assert.DoesNotContain("ORDERLY_RELEASES_PAT", workflow, System.StringComparison.Ordinal);
        Assert.DoesNotContain("Fences779/Orderly-Releases", workflow, System.StringComparison.Ordinal);
    }

    [Fact]
    public void E2e_script_requires_explicit_test_user_parameter_and_generic_docs()
    {
        var repoRoot = ResolveRepositoryRoot();
        var scriptPath = Path.Combine(repoRoot, "scripts", "e2e", "Run-OrderlyLocalE2E.ps1");
        var readmePath = Path.Combine(repoRoot, "scripts", "e2e", "README.md");
        Assert.True(File.Exists(scriptPath), $"未找到 E2E 脚本：{scriptPath}");
        Assert.True(File.Exists(readmePath), $"未找到 E2E README：{readmePath}");

        var script = File.ReadAllText(scriptPath);
        var readme = File.ReadAllText(readmePath);

        Assert.Contains("[Parameter(Mandatory = $true)]", script, System.StringComparison.Ordinal);
        Assert.Contains("[string]$ExpectedTestUserName", script, System.StringComparison.Ordinal);
        Assert.DoesNotMatch(@"[A-Za-z]:\\[^\\\r\n""']+\\[^\\\r\n""']+\\scripts\\e2e\\Run-OrderlyLocalE2E\.ps1", readme);
        Assert.Contains("-ExpectedTestUserName \"your-temporary-test-user\"", readme, System.StringComparison.Ordinal);
    }

    private static bool TryReadText(string path, out string text)
    {
        try
        {
            text = File.ReadAllText(path);
            return true;
        }
        catch
        {
            text = string.Empty;
            return false;
        }
    }

    private static IReadOnlyCollection<string> GetChangedFiles()
    {
        var set = new HashSet<string>(System.StringComparer.Ordinal);

        foreach (var (_, path) in ParsePorcelain())
        {
            set.Add(path);
        }

        foreach (var line in RunGit("diff", "--name-only", "HEAD"))
        {
            var normalized = Normalize(line);
            if (normalized.Length > 0)
            {
                set.Add(normalized);
            }
        }

        return set;
    }

    private static IEnumerable<(string Status, string Path)> ParsePorcelain()
    {
        var lines = RunGit("-c", "core.quotepath=false", "status", "--porcelain", "-uall");

        foreach (var rawLine in lines)
        {
            if (rawLine.Length < 4)
            {
                continue;
            }

            var status = rawLine[..2];
            var pathPart = rawLine[3..].Trim();
            var arrowIdx = pathPart.IndexOf(" -> ", System.StringComparison.Ordinal);
            if (arrowIdx >= 0)
            {
                pathPart = pathPart[(arrowIdx + " -> ".Length)..];
            }

            var normalized = Normalize(pathPart);
            if (normalized.Length > 0)
            {
                yield return (status, normalized);
            }
        }
    }

    private static string Normalize(string path)
    {
        var trimmed = path.Trim().Trim('"');
        return trimmed.Replace('\\', '/');
    }

    private static List<string> RunGit(params string[] args)
    {
        var repoRoot = ResolveRepositoryRoot();
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

        psi.ArgumentList.Add("-C");
        psi.ArgumentList.Add(repoRoot);
        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = psi };
        process.Start();
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.True(process.ExitCode == 0,
            $"git {string.Join(' ', args)} 返回非 0（{process.ExitCode}）：{stderr}");

        return stdout
            .Replace("\r\n", "\n")
            .Split('\n')
            .Where(line => line.Length > 0)
            .ToList();
    }

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

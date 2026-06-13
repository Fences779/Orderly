using System.Text;
using Xunit;

namespace Orderly.Tests.Regression;

/// <summary>
/// Regression guard for constraint C-4 / Requirement 11.5–11.9: the Main_Line
/// (<c>src/</c>, <c>tests/</c>, <c>tools/</c>, <c>README.md</c>, and <c>docs/</c>) must contain
/// zero occurrences of any Forbidden_Term.
/// </summary>
/// <remarks>
/// Per Requirement 11.8 this source file MUST NOT contain any literal Forbidden_Term, otherwise
/// the scan would report the test file itself as an offending location. Every term is therefore
/// assembled at runtime from two or more string fragments (<see cref="BuildForbiddenTerms"/>), so
/// the raw source text never holds a complete term contiguously.
///
/// TIMING (per tasks.md 1.2): it is OK and EXPECTED that this test FAILS today, because legacy
/// code still carries forbidden terms. The cleanup happens in later waves (gateway cleanup,
/// product identity cleanup, engineering cleanup, docs cleanup). This test only becomes
/// pass-gated during the final acceptance wave (tasks 24–25).
/// </remarks>
public sealed class ForbiddenTermsRegressionTests
{
    // Scan roots relative to the repository root. Directories are walked recursively; the single
    // file root (README.md) is scanned directly. The .kiro/ spec tree is never scanned (C-4 / 11.9).
    private static readonly string[] DirectoryRoots = { "src", "tests", "tools", "docs" };
    private static readonly string[] FileRoots = { "README.md" };

    // Directory names skipped anywhere in the tree: VCS metadata, IDE caches, and build output.
    // Build artifacts (bin/obj) are compiled output, not Main_Line source, and would otherwise
    // slow the scan and produce noise.
    private static readonly HashSet<string> SkippedDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".vs", ".kiro", "bin", "obj", "node_modules", "packages", "TestResults", "artifacts",
    };

    // Binary / non-text file extensions are skipped: scanning them is meaningless and risks
    // false positives against compiled byte sequences.
    private static readonly HashSet<string> SkippedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".dll", ".exe", ".pdb", ".png", ".jpg", ".jpeg", ".gif", ".ico", ".bmp", ".zip",
        ".7z", ".gz", ".tar", ".db", ".sqlite", ".bak", ".cache", ".nupkg", ".snk", ".pfx",
        ".woff", ".woff2", ".ttf", ".eot", ".pdf", ".mp4", ".mp3", ".wav",
        ".so", ".dylib", ".a", ".lib", ".o", ".obj", ".dat", ".bin", ".wasm",
    };

    [Fact]
    public void MainLine_ContainsNoForbiddenTerm()
    {
        var repoRoot = ResolveRepositoryRoot();
        var forbiddenTerms = BuildForbiddenTerms();

        var offences = new List<(string RelativePath, string Term)>();

        foreach (var file in EnumerateScannedFiles(repoRoot))
        {
            string relativePath = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');

            // A Forbidden_Term may appear in the file name/path itself (e.g. a launch script
            // whose name embeds a forbidden token), so the relative path is scanned in addition
            // to the file contents.
            foreach (var term in forbiddenTerms)
            {
                if (relativePath.Contains(term, StringComparison.OrdinalIgnoreCase))
                {
                    offences.Add((relativePath, term));
                }
            }

            string content;
            try
            {
                content = File.ReadAllText(file, Encoding.UTF8);
            }
            catch
            {
                // Unreadable / locked file: skip rather than fail the scan.
                continue;
            }

            foreach (var term in forbiddenTerms)
            {
                if (content.Contains(term, StringComparison.OrdinalIgnoreCase))
                {
                    offences.Add((relativePath, term));
                }
            }
        }

        if (offences.Count > 0)
        {
            var report = new StringBuilder();
            report.Append(offences.Count)
                  .Append(" forbidden-term occurrence(s) found in the Main_Line (src/, tests/, tools/, README.md, docs/):");
            report.AppendLine();
            foreach (var (path, term) in offences
                         .OrderBy(o => o.RelativePath, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(o => o.Term, StringComparer.OrdinalIgnoreCase))
            {
                report.Append("  - ").Append(path).Append("  ⟶  ").Append(term).AppendLine();
            }

            Assert.Fail(report.ToString());
        }
    }

    /// <summary>
    /// Walks upward from the test assembly location until it finds the directory containing
    /// <c>Orderly.sln</c>, which is treated as the repository root. Throws if no solution is found.
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
            "Could not locate the repository root (Orderly.sln) by walking up from " +
            AppContext.BaseDirectory + ".");
    }

    /// <summary>
    /// Enumerates every file under the configured scan roots, skipping VCS/IDE/build directories
    /// and known binary extensions. The .kiro/ spec tree is excluded via <see cref="SkippedDirectoryNames"/>.
    /// </summary>
    private static IEnumerable<string> EnumerateScannedFiles(string repoRoot)
    {
        foreach (var relativeRoot in DirectoryRoots)
        {
            var rootPath = Path.Combine(repoRoot, relativeRoot);
            if (!Directory.Exists(rootPath))
            {
                continue;
            }

            foreach (var file in EnumerateDirectoryFiles(rootPath))
            {
                yield return file;
            }
        }

        foreach (var relativeFile in FileRoots)
        {
            var filePath = Path.Combine(repoRoot, relativeFile);
            if (File.Exists(filePath))
            {
                yield return filePath;
            }
        }
    }

    private static IEnumerable<string> EnumerateDirectoryFiles(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var current = pending.Pop();

            string[] subdirectories;
            try
            {
                subdirectories = Directory.GetDirectories(current);
            }
            catch
            {
                subdirectories = Array.Empty<string>();
            }

            foreach (var subdirectory in subdirectories)
            {
                var name = Path.GetFileName(subdirectory);
                if (!SkippedDirectoryNames.Contains(name))
                {
                    pending.Push(subdirectory);
                }
            }

            string[] files;
            try
            {
                files = Directory.GetFiles(current);
            }
            catch
            {
                files = Array.Empty<string>();
            }

            foreach (var file in files)
            {
                if (!SkippedExtensions.Contains(Path.GetExtension(file)))
                {
                    yield return file;
                }
            }
        }
    }

    /// <summary>
    /// Builds the Forbidden_Terms list defined in requirements.md constraint C-4. Each term is
    /// assembled by concatenating two or more fragments so that no literal term ever appears in
    /// this source file (Requirement 11.8). No individual fragment equals a complete term.
    /// </summary>
    private static IReadOnlyList<string> BuildForbiddenTerms()
    {
        return new[]
        {
            "String" + "Narration",
            "串" + "述",
            "admin" + "Pc" + "Gateway",
            "brac" + "elet",
            "be" + "ad",
            "be" + "ad" + "s",
            "wr" + "ist",
            "wr" + "ist" + "Size",
            "dia" + "meter",
            "珠" + "串",
            "珠" + "子",
            "手" + "围",
            "直" + "径",
            "材" + "质",
            "订单" + "设计",
            "成" + "品",
            "平均" + "每" + "串",
            "Orderly" + "-" + "SN",
            "start" + "-" + "sn",
        };
    }
}

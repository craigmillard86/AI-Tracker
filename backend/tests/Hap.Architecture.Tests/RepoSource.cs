using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Hap.Architecture.Tests;

/// <summary>Locates the backend and frontend source trees at test time so architecture rules can
/// be asserted against the actual source files. Walks up from the test output directory to the
/// repo root (identified by containing both <c>backend/src</c> and <c>app/src</c>); throws
/// loudly if it cannot be found (a guard must never pass silently for lack of source).</summary>
internal static class RepoSource
{
    public static string RepoRootDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "backend", "src"))
                && Directory.Exists(Path.Combine(dir.FullName, "app", "src")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the repo root (containing both 'backend/src' and 'app/src') walking up from " +
            AppContext.BaseDirectory + " — the source-scan architecture guard cannot run.");
    }

    public static string BackendSrcDir() => Path.Combine(RepoRootDir(), "backend", "src");

    public static string BackendTestsDir() => Path.Combine(RepoRootDir(), "backend", "tests");

    public static string AppSrcDir() => Path.Combine(RepoRootDir(), "app", "src");

    public static IReadOnlyList<string> CsFiles() => CsFilesUnder(BackendSrcDir(), "backend/src");

    /// <summary>backend/tests, not backend/src — a separate scan target (panel round-1 advisory
    /// A3) so a source-scan guard can also cover test-project source, e.g. a hardcoded content
    /// string smuggled into a test file rather than production code.</summary>
    public static IReadOnlyList<string> TestCsFiles() => CsFilesUnder(BackendTestsDir(), "backend/tests");

    private static IReadOnlyList<string> CsFilesUnder(string root, string label)
    {
        var files = Directory
            .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .ToList();

        if (files.Count == 0)
        {
            throw new FileNotFoundException($"No .cs files found under {label} — source-scan guard cannot run.");
        }

        return files;
    }

    public static IReadOnlyList<string> TsFiles()
    {
        var files = Directory
            .EnumerateFiles(AppSrcDir(), "*.ts", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(AppSrcDir(), "*.tsx", SearchOption.AllDirectories))
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}")
                     && !p.Contains($"{Path.DirectorySeparatorChar}dist{Path.DirectorySeparatorChar}"))
            .ToList();

        if (files.Count == 0)
        {
            throw new FileNotFoundException("No .ts/.tsx files found under app/src — source-scan guard cannot run.");
        }

        return files;
    }
}

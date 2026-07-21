using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Hap.Architecture.Tests;

/// <summary>Locates the backend source tree at test time so architecture rules can be asserted
/// against the actual <c>.cs</c> files. Walks up from the test output directory to the folder
/// that contains <c>backend/src</c>; throws loudly if it cannot be found (a guard must never
/// pass silently for lack of source).</summary>
internal static class RepoSource
{
    public static string BackendSrcDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "backend", "src");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate 'backend/src' walking up from " + AppContext.BaseDirectory +
            " — the source-scan architecture guard cannot run.");
    }

    public static IReadOnlyList<string> CsFiles()
    {
        var files = Directory
            .EnumerateFiles(BackendSrcDir(), "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .ToList();

        if (files.Count == 0)
        {
            throw new FileNotFoundException("No .cs files found under backend/src — source-scan guard cannot run.");
        }

        return files;
    }
}

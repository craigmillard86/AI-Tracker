namespace Hap.Infrastructure.Frameworks;

/// <summary>
/// Resolves the default on-disk location of the seeded framework JSON when no explicit path is
/// configured. Mirrors <c>Program.cs</c>'s directory-snapshot fallback
/// (<c>Path.Combine(AppContext.BaseDirectory, "directory.json")</c>): it does not verify the
/// file exists — existence is checked lazily by <see cref="FrameworkSeeder"/> at the point of
/// use, so a missing file never blocks application startup, only an actual seed attempt.
/// Walking up from <see cref="AppContext.BaseDirectory"/> finds the real repo path in local
/// dev/test runs (the whole repo tree is present on disk); a container image that has not
/// copied <c>docs/frameworks</c> into its runtime layer will fail at first seed call, same
/// known gap as the directory-snapshot path for HAP-3.
/// </summary>
public static class FrameworkDefinitionLocator
{
    private const string RelativePath = "ai-maturity-sdlc.v1.json";

    public static string ResolveDefaultPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "docs", "frameworks", RelativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        // Nothing found walking up — return the best-guess relative-to-base-directory path so the
        // caller gets a clear FileNotFoundException naming this exact path when it actually tries
        // to read it, rather than an opaque failure here at resolution time.
        return Path.Combine(AppContext.BaseDirectory, "docs", "frameworks", RelativePath);
    }
}

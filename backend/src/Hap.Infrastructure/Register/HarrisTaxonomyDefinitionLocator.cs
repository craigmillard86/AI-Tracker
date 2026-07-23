namespace Hap.Infrastructure.Register;

/// <summary>
/// Resolves the default on-disk location of the seeded Harris taxonomy JSON when no explicit path is
/// configured. Mirrors <see cref="Frameworks.FrameworkDefinitionLocator"/> exactly: it does NOT verify
/// the file exists (existence is checked lazily by <see cref="HarrisTaxonomySeeder"/> at the point of
/// use), and walking up from <see cref="AppContext.BaseDirectory"/> finds the real repo path in local
/// dev/test runs. Same known container gap as the framework locator — an image that has not copied
/// <c>docs/frameworks</c> into its runtime layer fails at first seed call, not startup.
/// </summary>
public static class HarrisTaxonomyDefinitionLocator
{
    private const string RelativePath = "harris-taxonomy.v1.json";

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

        return Path.Combine(AppContext.BaseDirectory, "docs", "frameworks", RelativePath);
    }
}

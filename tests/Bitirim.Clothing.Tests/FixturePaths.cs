using Xunit;

namespace Bitirim.Clothing.Tests;

/// <summary>
/// Locates the local fixture set.
/// </summary>
/// <remarks>
/// Fixtures are extracted from the developer's own legal GTA V installation and
/// deliberately live <em>outside</em> the repository: no Rockstar asset is ever
/// committed (docs/legal-and-oss.md rule 5). Tests that need them skip cleanly
/// when they are absent, so a fresh clone still builds and runs green.
/// </remarks>
public static class FixturePaths
{
    public static string Root =>
        Environment.GetEnvironmentVariable("BCC_FIXTURES") ?? @"C:\bcc\fixtures";

    public static string Ydd => Path.Combine(Root, "jbib_000_u.ydd");
    public static string YtdA => Path.Combine(Root, "jbib_diff_000_a_uni.ytd");
    public static string YtdB => Path.Combine(Root, "jbib_diff_000_b_uni.ytd");
    public static string Ymt => Path.Combine(Root, "mp_m_freemode_01.ymt");

    public static bool Available =>
        File.Exists(Ydd) && File.Exists(YtdA) && File.Exists(Ymt);

    public static string PythonExe =>
        Environment.GetEnvironmentVariable("BCC_PYTHON") ?? @"C:\bcc\python\python.exe";

    /// <summary>
    /// The source tree this assembly was compiled from, baked in by the csproj.
    /// </summary>
    /// <remarks>
    /// Build output lives at <c>C:\bcc\build</c> (see Directory.Build.props), so walking
    /// upward from <see cref="AppContext.BaseDirectory"/> never reaches the repository.
    /// </remarks>
    public static string RepoRoot
    {
        get
        {
            var baked = typeof(FixturePaths).Assembly
                .GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
                .Cast<System.Reflection.AssemblyMetadataAttribute>()
                .FirstOrDefault(a => a.Key == "RepoRoot")?.Value;
            return string.IsNullOrEmpty(baked) ? AppContext.BaseDirectory : Path.GetFullPath(baked);
        }
    }

    public static string AssetService
    {
        get
        {
            var explicitPath = Environment.GetEnvironmentVariable("BCC_ASSETSERVICE");
            if (!string.IsNullOrEmpty(explicitPath)) return explicitPath;

            var baked = Path.Combine(RepoRoot, "src", "assetservice", "main.py");
            if (File.Exists(baked)) return baked;

            var dir = AppContext.BaseDirectory;
            for (var i = 0; i < 10 && dir is not null; i++)
            {
                var candidate = Path.Combine(dir, "src", "assetservice", "main.py");
                if (File.Exists(candidate)) return candidate;
                dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
            }
            return string.Empty;
        }
    }

    public static bool BackendAvailable =>
        File.Exists(PythonExe) && File.Exists(AssetService);
}

/// <summary>Skips a test when the local fixture set or backend is missing.</summary>
public sealed class RequiresFixturesFactAttribute : FactAttribute
{
    public RequiresFixturesFactAttribute()
    {
        if (!FixturePaths.Available)
            Skip = $"GTA fixtures not present at {FixturePaths.Root}. " +
                   "Extract them from your own game install; see docs/phase-1-results.md.";
        else if (!FixturePaths.BackendAvailable)
            Skip = "Asset service or Python runtime not found. Set BCC_PYTHON / BCC_ASSETSERVICE.";
    }
}

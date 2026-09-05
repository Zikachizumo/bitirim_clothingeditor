namespace Bitirim.Clothing.Editor.Infrastructure;

/// <summary>
/// Every location the application writes to.
/// </summary>
/// <remarks>
/// Installed builds keep user data under %APPDATA%. Portable builds keep it
/// beside the executable, so unzipping to a USB stick and running leaves
/// nothing behind on the host machine.
/// </remarks>
public static class Paths
{
    private static string? _rootOverride;

    /// <summary>
    /// True when a <c>portable.marker</c> file sits next to the executable.
    /// </summary>
    public static bool IsPortable { get; } =
        File.Exists(Path.Combine(AppContext.BaseDirectory, "portable.marker"));

    /// <summary>
    /// Where user data lives.
    /// </summary>
    /// <remarks>
    /// <c>BCC_DATA_ROOT</c> redirects it, which is how the test suite keeps its
    /// logs, workspaces and caches out of the developer's real application
    /// data. A test run must not leave anything behind in a folder the user
    /// looks at.
    /// </remarks>
    public static string Root => _rootOverride ??=
        Environment.GetEnvironmentVariable("BCC_DATA_ROOT") is { Length: > 0 } overridden
            ? overridden
            : IsPortable
                ? Path.Combine(AppContext.BaseDirectory, "userdata")
                : Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Bitirim", "ClothingCreator");

    public static string Logs => Ensure(Path.Combine(Root, "logs"));
    public static string Settings => Ensure(Root);
    public static string Cache => Ensure(Path.Combine(Root, "cache"));
    public static string Thumbnails => Ensure(Path.Combine(Cache, "thumbnails"));
    public static string Temp => Ensure(Path.Combine(Root, "temp"));

    /// <summary>Scratch space for projects opened from a .bitirimclothing package.</summary>
    public static string Workspaces => Ensure(Path.Combine(Root, "workspaces"));

    /// <summary>Autosave snapshots, used for crash recovery.</summary>
    public static string Recovery => Ensure(Path.Combine(Root, "recovery"));

    public static string DefaultProjectsFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "Bitirim", "Clothing Projects");

    /// <summary>The bundled Python runtime. Ships inside the application folder.</summary>
    public static string BundledPython =>
        Environment.GetEnvironmentVariable("BCC_PYTHON")
        ?? Path.Combine(AppContext.BaseDirectory, "runtime", "python", "python.exe");

    /// <summary>The asset service entry point.</summary>
    public static string AssetService =>
        Environment.GetEnvironmentVariable("BCC_ASSETSERVICE")
        ?? Path.Combine(AppContext.BaseDirectory, "runtime", "assetservice", "main.py");

    /// <summary>Static web assets for the WebView.</summary>
    public static string WebRoot => Path.Combine(AppContext.BaseDirectory, "webui");

    public static string NewTempFile(string extension) =>
        Path.Combine(Temp, $"{Guid.NewGuid():N}{extension}");

    public static void CleanTemp()
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(Temp))
            {
                try { File.Delete(f); } catch { /* in use; harmless */ }
            }
        }
        catch { /* nothing to clean */ }
    }

    private static string Ensure(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }
}

using Bitirim.Clothing.Core.Naming;
using Bitirim.Clothing.Editor.Infrastructure;

namespace Bitirim.Clothing.Editor.Library;

public sealed record LibraryAsset(
    string Id,
    string Name,
    string Path,
    string Kind,
    bool Male,
    PedComponent? Component,
    int? DrawableIndex,
    long SizeBytes,
    DateTimeOffset Modified,
    IReadOnlyList<string> TexturePaths,
    bool IsMock);

public sealed record LibraryScanResult(
    IReadOnlyList<LibraryAsset> Assets,
    string? Root,
    int Scanned,
    string? Note);

/// <summary>
/// Discovers clothing drawables on disk.
/// </summary>
/// <remarks>
/// Scans a folder tree for <c>.ydd</c> files and pairs each with the matching
/// <c>.ytd</c> variants by name. Gender and component are inferred from the
/// filename and the folder path -- both are reported as <c>null</c> when they
/// cannot be determined rather than defaulted, so the UI can show "--" instead
/// of a confident wrong answer.
/// </remarks>
public sealed class AssetLibraryService
{
    private const int MaxAssets = 5000;

    private readonly LogService _log;

    public AssetLibraryService(LogService log) => _log = log;

    public LibraryScanResult Scan(string? root)
    {
        if (string.IsNullOrWhiteSpace(root))
            return new LibraryScanResult(Array.Empty<LibraryAsset>(), null, 0,
                "No asset library folder is configured. Set one in Settings, or import a .ydd directly.");

        if (!Directory.Exists(root))
            return new LibraryScanResult(Array.Empty<LibraryAsset>(), root, 0,
                $"The asset library folder does not exist: {root}");

        var assets = new List<LibraryAsset>();
        var scanned = 0;

        try
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                MaxRecursionDepth = 8,
            };

            foreach (var ydd in Directory.EnumerateFiles(root, "*.ydd", options))
            {
                scanned++;
                if (assets.Count >= MaxAssets) break;
                assets.Add(Describe(ydd, root));
            }
        }
        catch (Exception ex)
        {
            _log.Error($"Asset library scan failed under {root}", ex);
            return new LibraryScanResult(assets, root, scanned,
                "The library could not be fully scanned. Some folders may be inaccessible.");
        }

        _log.Info($"Asset library: {assets.Count} drawable(s) under {root}");
        var note = assets.Count >= MaxAssets
            ? $"Showing the first {MaxAssets} drawables. Narrow the library folder to see the rest."
            : null;

        return new LibraryScanResult(assets, root, scanned, note);
    }

    private static LibraryAsset Describe(string yddPath, string root)
    {
        var info = new FileInfo(yddPath);
        var stem = Path.GetFileNameWithoutExtension(yddPath);
        var directory = Path.GetDirectoryName(yddPath)!;
        var relative = Path.GetRelativePath(root, yddPath).Replace('\\', '/');

        var (component, drawable) = ParseDrawableName(stem);
        var male = InferMale(relative);

        // Textures for jbib_004_u are jbib_diff_004_*.ytd in the same folder.
        var textures = new List<string>();
        if (component is not null && drawable is not null)
        {
            var pattern = $"{ClothingNames.Prefix(component.Value)}_diff_{drawable.Value:D3}_*.ytd";
            try
            {
                textures.AddRange(Directory.EnumerateFiles(directory, pattern).OrderBy(p => p));
            }
            catch { /* unreadable folder; leave empty */ }
        }

        return new LibraryAsset(
            Id: relative,
            Name: stem,
            Path: yddPath,
            Kind: "ydd",
            Male: male ?? true,
            Component: component,
            DrawableIndex: drawable,
            SizeBytes: info.Length,
            Modified: info.LastWriteTimeUtc,
            TexturePaths: textures,
            IsMock: false);
    }

    /// <summary>
    /// Parses <c>jbib_004_u</c> into its component and drawable index.
    /// </summary>
    public static (PedComponent? Component, int? Drawable) ParseDrawableName(string stem)
    {
        var parts = stem.Split('_');
        if (parts.Length < 2) return (null, null);
        if (!ClothingNames.TryParsePrefix(parts[0], out var component)) return (null, null);
        if (!int.TryParse(parts[1], out var drawable)) return (component, null);
        return (component, drawable);
    }

    /// <summary>
    /// Guesses gender from the path. Returns null when nothing in the path says.
    /// </summary>
    private static bool? InferMale(string relativePath)
    {
        var p = relativePath.ToLowerInvariant();
        if (p.Contains("mp_m_freemode") || p.Contains("/male/") || p.StartsWith("male/")) return true;
        if (p.Contains("mp_f_freemode") || p.Contains("/female/") || p.StartsWith("female/")) return false;
        return null;
    }
}

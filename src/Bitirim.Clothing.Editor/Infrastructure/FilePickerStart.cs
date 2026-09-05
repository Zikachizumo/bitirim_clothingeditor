using System.Security;

namespace Bitirim.Clothing.Editor.Infrastructure;

/// <summary>
/// Chooses the folder a file picker should open in.
/// </summary>
/// <remarks>
/// Game files come from the asset library, so the first Browse of a run should
/// land there rather than wherever Explorer happened to be. After that the
/// picker follows the user: being yanked back to the library on every pick is
/// worse than not being taken there at all.
///
/// The result is always a canonical Windows path. The shell dialog resolves
/// <c>InitialDirectory</c> through <c>SHCreateItemFromParsingName</c>, which
/// rejects a forward-slash path outright -- and the failure is not a mis-placed
/// dialog but an exception that stops the dialog opening at all. The library
/// path is user-entered and stored as typed (<c>C:/bcc/fixtures</c> is what the
/// settings file actually held), so it cannot be handed over unnormalised.
/// </remarks>
public static class FilePickerStart
{
    /// <summary>Picker kinds whose files live in the asset library.</summary>
    private static readonly HashSet<string> GameAssetKinds =
        new(StringComparer.OrdinalIgnoreCase) { "ydd", "ytd", "ymt", "asset" };

    /// <summary>Which slot of the caller's history a kind reads and writes.</summary>
    /// <remarks>
    /// The game-asset kinds share one slot because they are picked together: a
    /// garment's .ydd and its .ytd sit in the same DLC folder, so having just
    /// chosen the drawable there is the strongest possible hint about where the
    /// texture is. Images keep their own slot -- reference art has nothing to do
    /// with where the game files were.
    /// </remarks>
    public static string GroupKey(string? kind) =>
        kind is not null && GameAssetKinds.Contains(kind) ? "game-asset" : kind ?? string.Empty;

    /// <param name="kind">Picker kind, e.g. <c>ydd</c> or <c>image</c>.</param>
    /// <param name="remembered">Where each kind was last pointed.</param>
    /// <param name="libraryPath">The configured asset library, as stored.</param>
    /// <param name="directoryExists">Existence check; injected for testing.</param>
    /// <returns>A canonical directory, or null to let the dialog decide.</returns>
    public static string? Resolve(
        string? kind,
        IReadOnlyDictionary<string, string> remembered,
        string? libraryPath,
        Func<string, bool> directoryExists)
    {
        ArgumentNullException.ThrowIfNull(remembered);
        ArgumentNullException.ThrowIfNull(directoryExists);

        kind ??= string.Empty;

        if (remembered.TryGetValue(GroupKey(kind), out var last) && Canonical(last) is { } lastPath
            && Safely(directoryExists, lastPath))
            return lastPath;

        if (!GameAssetKinds.Contains(kind)) return null;

        return Canonical(libraryPath) is { } library && Safely(directoryExists, library)
            ? library
            : null;
    }

    /// <summary>
    /// A full path with the platform's separators, or null when the string is
    /// not a usable path at all.
    /// </summary>
    private static string? Canonical(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException
                                      or PathTooLongException or SecurityException)
        {
            return null;
        }
    }

    /// <summary>
    /// A folder that cannot be probed -- a disconnected share, a drive that is
    /// gone -- must not take the picker down with it.
    /// </summary>
    private static bool Safely(Func<string, bool> exists, string path)
    {
        try { return exists(path); }
        catch { return false; }
    }
}

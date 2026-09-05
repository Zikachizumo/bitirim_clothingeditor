namespace Bitirim.Clothing.Editor.Export;

/// <summary>What an export preset produces.</summary>
public enum ExportKind
{
    /// <summary>A FiveM addon clothing resource: stream/, fxmanifest.lua, README.</summary>
    FiveMResource,

    /// <summary>The composited textures only, as PNG, for handing to someone else.</summary>
    TexturePack,

    /// <summary>The whole project as a .bitirimclothing package.</summary>
    ProjectArchive,

    /// <summary>Resource plus the project archive plus a validation report.</summary>
    DevelopmentPackage,
}

/// <summary>
/// One named export configuration.
/// </summary>
/// <remarks>
/// Presets exist so the wizard's later steps can be pre-answered, not to hide
/// what is happening: every preset still runs the same validation and the same
/// writer, and the wizard shows the settings it filled in.
/// </remarks>
public sealed record ExportPreset(
    string Id,
    string Name,
    string Description,
    ExportKind Kind,
    bool RequiresRealAsset,
    bool WritesGameFiles)
{
    /// <summary>
    /// Whether this preset produces files a game would load. Presets that do
    /// carry the experimental warning; presets that do not (a texture pack, an
    /// archive) are ordinary file output and must not be labelled as if they
    /// were unverified game assets.
    /// </summary>
    public bool Experimental => WritesGameFiles;
}

public static class ExportPresets
{
    public static readonly ExportPreset FiveMResource = new(
        "fivem-resource",
        "FiveM Resource",
        "An addon clothing resource: stream/ with the .ydd, .ytd and .ymt, plus fxmanifest.lua.",
        ExportKind.FiveMResource,
        RequiresRealAsset: true,
        WritesGameFiles: true);

    public static readonly ExportPreset TexturePack = new(
        "texture-pack",
        "Texture Pack",
        "The composited texture of every variation as PNG. No game files.",
        ExportKind.TexturePack,
        RequiresRealAsset: false,
        WritesGameFiles: false);

    public static readonly ExportPreset ProjectArchive = new(
        "project-archive",
        "Project Archive",
        "The whole project as a single .bitirimclothing file, for backup or handover.",
        ExportKind.ProjectArchive,
        RequiresRealAsset: false,
        WritesGameFiles: false);

    public static readonly ExportPreset DevelopmentPackage = new(
        "development-package",
        "Development Package",
        "The resource, the project archive and a written validation report together.",
        ExportKind.DevelopmentPackage,
        RequiresRealAsset: true,
        WritesGameFiles: true);

    public static readonly IReadOnlyList<ExportPreset> All = new[]
    {
        FiveMResource, TexturePack, ProjectArchive, DevelopmentPackage,
    };

    public static ExportPreset Resolve(string? id) =>
        All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase))
        ?? FiveMResource;
}

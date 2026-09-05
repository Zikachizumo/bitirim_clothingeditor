using Bitirim.Clothing.Core.Naming;
using Bitirim.Clothing.Core.Rage;
using Bitirim.Clothing.Editor.Projects;
using Bitirim.Clothing.Validation;

namespace Bitirim.Clothing.Editor.Validation;

/// <summary>What the validator was able to read about one garment.</summary>
public sealed record AssetEvidence(
    ClothingAsset Asset,
    string? ResolvedYdd,
    string? ResolvedYtd,
    DrawableDictionaryInfo? Drawable,
    TextureDictionaryInfo? Texture,
    string? ReadFailure);

/// <summary>
/// Whole-project validation.
/// </summary>
/// <remarks>
/// Split out of the host operation so it can be tested without a window, a
/// WebView or a Python process. Every rule states what is wrong, and where
/// possible what to do about it: a validation panel that only says "invalid"
/// trains people to ignore it.
///
/// Rules that would require capabilities this build does not have are reported
/// as facts about the build, not as project defects. "The YDD writer is
/// unavailable" is an INFO about what cannot be edited, not an ERROR against
/// the user's work.
/// </remarks>
public sealed class ProjectValidator
{
    private readonly ValidationEngine _engine;

    public ProjectValidator(ValidationEngine engine) => _engine = engine;

    public ValidationReport Validate(
        ClothingProject document,
        IReadOnlyList<AssetEvidence> assets,
        BackendCapabilities? capabilities)
    {
        var findings = new List<Finding>();

        ValidateProject(document, findings);
        foreach (var evidence in assets) ValidateAsset(document, evidence, findings);
        ValidateExport(document, assets, findings);
        ReportBuildLimits(capabilities, findings);

        return new ValidationReport(findings);
    }

    // ------------------------------------------------------------------

    private static void ValidateProject(ClothingProject document, List<Finding> findings)
    {
        if (document.Assets.Count == 0)
        {
            findings.Add(new Finding(Severity.Error, "project.noassets",
                "This project contains no garments.")
            { Target = "tab:model" });
            return;
        }

        if (string.IsNullOrWhiteSpace(document.Name))
            findings.Add(new Finding(Severity.Warning, "project.noname",
                "The project has no name.") { Target = "setting:name" });

        if (string.IsNullOrEmpty(document.YmtTemplatePath))
        {
            findings.Add(new Finding(Severity.Error, "ymt.notemplate",
                "No ped metadata template has been set for this project.",
                "Addon metadata is derived from a real ped .ymt; import one from your game files.")
            { Target = "setting:ymtTemplate" });
        }

        // Two garments claiming the same slot would overwrite each other on
        // export, and the second one silently wins. Catch it here instead.
        var clashes = document.Assets
            .Where(a => a.IncludeInExport)
            .GroupBy(a => (a.Component, a.DrawableIndex))
            .Where(g => g.Count() > 1);

        foreach (var clash in clashes)
        {
            findings.Add(new Finding(Severity.Error, "project.slotclash",
                $"{clash.Count()} garments target component "
                + $"{ClothingNames.Prefix(clash.Key.Component)} drawable {clash.Key.DrawableIndex}: "
                + string.Join(", ", clash.Select(a => $"'{a.Name}'")) + ".",
                "Give each garment its own drawable index, or exclude one from the export.")
            { Target = $"asset:{clash.First().Id}" });
        }
    }

    private void ValidateAsset(ClothingProject document, AssetEvidence evidence, List<Finding> findings)
    {
        var asset = evidence.Asset;
        var where = $"asset:{asset.Id}";
        var label = document.Assets.Count > 1 ? $"'{asset.Name}': " : "";

        if (asset.IsMock)
        {
            findings.Add(new Finding(Severity.Error, "project.mock",
                $"{label}This garment uses a synthetic mock asset, which cannot be exported "
                + "as a FiveM resource.",
                "Import a real .ydd drawable to enable export.") { Target = where });
            return;
        }

        if (string.IsNullOrEmpty(asset.BaseYddPath))
        {
            findings.Add(new Finding(Severity.Error, "project.noydd",
                $"{label}No base drawable has been set.") { Target = where });
        }
        else if (evidence.ResolvedYdd is null || !File.Exists(evidence.ResolvedYdd))
        {
            findings.Add(new Finding(Severity.Error, "asset.missingydd",
                $"{label}The base drawable is missing from the project folder.",
                "Re-import the .ydd, or restore assets/ from a backup.") { Target = where });
        }

        if (string.IsNullOrEmpty(asset.BaseYtdPath))
        {
            findings.Add(new Finding(Severity.Error, "project.noytd",
                $"{label}No base texture dictionary has been set.") { Target = where });
        }
        else if (evidence.ResolvedYtd is null || !File.Exists(evidence.ResolvedYtd))
        {
            findings.Add(new Finding(Severity.Error, "asset.missingytd",
                $"{label}The base texture dictionary is missing from the project folder.")
            { Target = where });
        }

        if (evidence.ReadFailure is not null)
        {
            findings.Add(new Finding(Severity.Warning, "validate.backend",
                $"{label}The drawable could not be checked: {evidence.ReadFailure}")
            { Target = where });
        }

        if (evidence.Drawable is not null)
        {
            foreach (var finding in _engine
                         .ValidateDrawableForExport(evidence.Drawable, asset.Component, asset.DrawableIndex)
                         .Findings)
            {
                findings.Add(finding with { Message = label + finding.Message, Target = where });
            }

            ValidateUvAndMaterials(evidence.Drawable, label, where, findings);
        }

        if (asset.Variations.Count == 0)
        {
            findings.Add(new Finding(Severity.Error, "project.novariation",
                $"{label}The garment has no texture variations.") { Target = where });
        }

        foreach (var variation in asset.Variations)
        {
            if (string.IsNullOrEmpty(variation.TexturePath) && asset.IncludeInExport)
            {
                findings.Add(new Finding(Severity.Warning, "texture.unsaved",
                    $"{label}Variation '{variation.Name}' has no saved texture yet.",
                    "Open the Texture tab and save it before exporting.")
                { Target = $"variation:{variation.Id}" });
            }

            var missingImages = variation.Layers
                .Where(l => l.Kind is LayerKind.Image or LayerKind.Generated)
                .Count(l => string.IsNullOrEmpty(l.Source));

            if (missingImages > 0)
            {
                findings.Add(new Finding(Severity.Warning, "texture.layerbroken",
                    $"{label}'{variation.Name}' has {missingImages} image layer(s) with no file.")
                { Target = $"variation:{variation.Id}" });
            }
        }
    }

    /// <summary>
    /// UV and material facts read straight off the drawable.
    /// </summary>
    /// <remarks>
    /// These are reported as INFO because they describe the asset the user
    /// imported, not a mistake they made. They are here because the panel is
    /// where people look to find out what an asset actually contains.
    /// </remarks>
    private static void ValidateUvAndMaterials(
        DrawableDictionaryInfo ydd, string label, string where, List<Finding> findings)
    {
        var drawable = ydd.Drawables.FirstOrDefault();
        if (drawable is null) return;

        if (drawable.Lods.TryGetValue("high", out var high))
        {
            if (high.UvSets.Count == 0 || high.UvSets.All(n => n == 0))
            {
                findings.Add(new Finding(Severity.Error, "uv.missing",
                    $"{label}The high LOD carries no UV coordinates, so it cannot be textured.")
                { Target = "tab:uv" });
            }
            else if (high.UvSets.Any(n => n > 1))
            {
                findings.Add(new Finding(Severity.Info, "uv.multiset",
                    $"{label}Some meshes carry {high.UvSets.Max()} UV sets. "
                    + "Only the first is used for the diffuse.") { Target = "tab:uv" });
            }

            if (high.Normals == 0)
                findings.Add(new Finding(Severity.Warning, "mesh.nonormals",
                    $"{label}The high LOD has no vertex normals; lighting will be flat.")
                { Target = "tab:model" });

            if (high.Tangents == 0)
                findings.Add(new Finding(Severity.Info, "mesh.notangents",
                    $"{label}The high LOD has no tangents, so the normal map will not light correctly.")
                { Target = "tab:material" });
        }

        var shaders = drawable.Materials.Select(m => m.Shader).Distinct().ToList();
        foreach (var shader in shaders.Where(s => !s.Contains("ped", StringComparison.OrdinalIgnoreCase)))
        {
            findings.Add(new Finding(Severity.Warning, "material.shader",
                $"{label}Material uses shader '{shader}', which is not a ped shader.",
                "Clothing normally uses a ped_* shader; other shaders may not receive skinning.")
            { Target = "tab:material" });
        }

        var hasNormal = drawable.Materials.SelectMany(m => m.Textures)
            .Any(t => t.Parameter.Contains("Bump", StringComparison.OrdinalIgnoreCase)
                      || t.Parameter.Contains("Normal", StringComparison.OrdinalIgnoreCase));
        if (!hasNormal)
        {
            findings.Add(new Finding(Severity.Info, "material.nonormalmap",
                $"{label}No material references a normal map.") { Target = "tab:material" });
        }

        _ = where;
    }

    private static void ValidateExport(
        ClothingProject document, IReadOnlyList<AssetEvidence> assets, List<Finding> findings)
    {
        var export = document.Export;

        if (string.IsNullOrWhiteSpace(export.ResourceName))
        {
            findings.Add(new Finding(Severity.Error, "export.noresourcename",
                "The export has no resource name.") { Target = "setting:resourceName" });
        }
        else if (!IsSafeIdentifier(export.ResourceName))
        {
            findings.Add(new Finding(Severity.Error, "export.resourcename",
                $"'{export.ResourceName}' is not a usable resource folder name.",
                "Use lower-case letters, digits, underscores and hyphens only.")
            { Target = "setting:resourceName" });
        }

        if (string.IsNullOrWhiteSpace(export.DlcName))
        {
            findings.Add(new Finding(Severity.Error, "export.nodlcname",
                "The export has no DLC name.") { Target = "setting:dlcName" });
        }
        else if (!IsSafeIdentifier(export.DlcName))
        {
            findings.Add(new Finding(Severity.Error, "export.dlcname",
                $"'{export.DlcName}' is not a usable DLC name.",
                "The DLC name becomes part of every streamed file name; "
                + "use lower-case letters, digits and underscores.") { Target = "setting:dlcName" });
        }
        else
        {
            // <ped>_<dlc>^<asset>.ytd has to survive the game's own path
            // handling; long names are a known source of silent load failures.
            var longest = assets
                .Select(a => $"{document.Ped}_{export.DlcName}^"
                             + ClothingNames.DiffuseTexture(a.Asset.Component, a.Asset.DrawableIndex)
                             + ".ytd")
                .OrderByDescending(n => n.Length)
                .FirstOrDefault();

            if (longest is not null && longest.Length > 100)
            {
                findings.Add(new Finding(Severity.Warning, "export.longname",
                    $"The longest streamed file name is {longest.Length} characters: {longest}",
                    "Shorten the DLC name; very long asset names are a known cause of "
                    + "assets failing to stream.") { Target = "setting:dlcName" });
            }
        }

        if (!string.Equals(export.TextureFormat, "BC7", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(export.TextureFormat, "BC3", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(export.TextureFormat, "BC1", StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(new Finding(Severity.Error, "export.format",
                $"'{export.TextureFormat}' is not a texture format this build can write.",
                "Supported: BC1, BC3, BC7.") { Target = "setting:textureFormat" });
        }

        var exportable = assets.Count(a => a.Asset.IncludeInExport && !a.Asset.IsMock);
        if (exportable == 0)
        {
            findings.Add(new Finding(Severity.Error, "export.nothing",
                "No garment in this project is selected for export.") { Target = "tab:validation" });
        }
    }

    private static void ReportBuildLimits(BackendCapabilities? capabilities, List<Finding> findings)
    {
        if (capabilities is null)
        {
            findings.Add(new Finding(Severity.Error, "validate.nobackend",
                "The asset engine is not running, so nothing could be checked against the real files.",
                "Real GTA assets cannot be read or written in this state."));
            return;
        }

        if (!capabilities.WriteYdd)
        {
            findings.Add(new Finding(Severity.Info, "asset.yddreadonly",
                "The drawable is copied byte-for-byte on export; this build does not rewrite .ydd files.",
                "Mesh, UV and weight editing are unavailable for that reason. "
                + "See docs/known-limitations.md."));
        }

        findings.Add(new Finding(Severity.Info, "export.unverified",
            "Exported resources have not been validated in a running FiveM client.",
            "Treat every export from this build as experimental."));
    }

    private static bool IsSafeIdentifier(string value) =>
        value.Length is > 0 and <= 64
        && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-')
        && char.IsAsciiLetter(value[0]);
}

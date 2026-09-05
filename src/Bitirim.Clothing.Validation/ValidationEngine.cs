using Bitirim.Clothing.Core.Naming;
using Bitirim.Clothing.Core.Rage;

namespace Bitirim.Clothing.Validation;

public enum Severity { Info, Warning, Error }

/// <summary>
/// Where a finding belongs in the validation panel.
/// </summary>
/// <remarks>
/// Categories exist so a long report stays readable, not to add ceremony:
/// a user chasing a red export wants the metadata findings together, not
/// interleaved with texture warnings.
/// </remarks>
public static class FindingCategories
{
    public const string Project = "Project";
    public const string Asset = "Asset";
    public const string Mesh = "Mesh";
    public const string Uv = "UV";
    public const string Texture = "Texture";
    public const string Material = "Material";
    public const string Metadata = "Metadata";
    public const string Export = "Export";

    public static readonly IReadOnlyList<string> All = new[]
    {
        Project, Asset, Mesh, Uv, Texture, Material, Metadata, Export,
    };

    /// <summary>Derives a category from a finding code such as <c>texture.mips</c>.</summary>
    public static string For(string code)
    {
        var prefix = code.Split('.', 2)[0].ToLowerInvariant();
        return prefix switch
        {
            "project" => Project,
            "ydd" or "asset" or "drawable" => Asset,
            "mesh" or "lod" or "skin" => Mesh,
            "uv" => Uv,
            "texture" or "ytd" => Texture,
            "material" or "shader" => Material,
            "ymt" or "metadata" => Metadata,
            "export" or "resource" or "manifest" or "validate" => Export,
            _ => Project,
        };
    }
}

public sealed record Finding(Severity Severity, string Code, string Message, string? Hint = null)
{
    private readonly string? _category;

    /// <summary>
    /// Panel grouping. Falls back to the code's prefix, so a rule added later
    /// lands somewhere sensible without every call site being edited.
    /// </summary>
    public string Category
    {
        get => _category ?? FindingCategories.For(Code);
        init => _category = value;
    }

    /// <summary>
    /// What the finding is about, so the panel row can navigate to it. One of
    /// <c>tab:&lt;name&gt;</c>, <c>asset:&lt;id&gt;</c>, <c>variation:&lt;id&gt;</c>,
    /// <c>layer:&lt;id&gt;</c>, or <c>setting:&lt;name&gt;</c>.
    /// </summary>
    public string? Target { get; init; }
}

public sealed record ValidationReport(IReadOnlyList<Finding> Findings)
{
    public bool HasErrors => Findings.Any(f => f.Severity == Severity.Error);
    public bool HasWarnings => Findings.Any(f => f.Severity == Severity.Warning);

    /// <summary>Green when clean, yellow on warnings, red on errors.</summary>
    public string Status => HasErrors ? "RED" : HasWarnings ? "YELLOW" : "GREEN";
}

/// <summary>
/// Pre-export checks. Errors block an export; warnings require confirmation.
/// </summary>
/// <remarks>
/// Every rule here is structural and cheap. Rules that would need skinning or
/// shader analysis are deliberately absent rather than approximated -- a check
/// that guesses is worse than no check, because it teaches the user to ignore
/// the panel.
/// </remarks>
public sealed class ValidationEngine
{
    public ValidationReport ValidateTextureForExport(TextureInfo texture, string expectedName)
    {
        var findings = new List<Finding>();

        if (!string.Equals(texture.Name, expectedName, StringComparison.Ordinal))
            findings.Add(new Finding(Severity.Error, "texture.name",
                $"Texture is named '{texture.Name}' but the drawable's material looks up '{expectedName}'.",
                "The name inside the dictionary must match the material's DiffuseSampler reference."));

        if (!IsPowerOfTwo(texture.Width) || !IsPowerOfTwo(texture.Height))
            findings.Add(new Finding(Severity.Error, "texture.npot",
                $"Texture is {texture.Width}x{texture.Height}; dimensions must be powers of two."));

        if (texture.Width != texture.Height)
            findings.Add(new Finding(Severity.Warning, "texture.nonsquare",
                $"Texture is {texture.Width}x{texture.Height}. Non-square clothing textures work, "
                + "but skin-swap garments require square dimensions."));

        if (texture.MipCount <= 1)
            findings.Add(new Finding(Severity.Warning, "texture.mips",
                "Texture has no mip chain, which causes shimmering at distance."));

        if (texture.Width > 2048 || texture.Height > 2048)
            findings.Add(new Finding(Severity.Warning, "texture.large",
                $"Texture is {texture.Width}x{texture.Height}. Above 2048 costs VRAM on every "
                + "player wearing the garment."));

        return new ValidationReport(findings);
    }

    public ValidationReport ValidateDrawableForExport(
        DrawableDictionaryInfo ydd, PedComponent component, int drawableIndex)
    {
        var findings = new List<Finding>();

        if (ydd.DrawableCount == 0)
        {
            findings.Add(new Finding(Severity.Error, "ydd.empty",
                "Drawable dictionary contains no drawables."));
            return new ValidationReport(findings);
        }

        var drawable = ydd.Drawables[0];

        foreach (var required in new[] { "high", "med", "low" })
        {
            if (!drawable.Lods.ContainsKey(required))
                findings.Add(new Finding(Severity.Warning, "ydd.lod",
                    $"Drawable has no '{required}' LOD. GTA clothing normally ships three.",
                    "Missing LODs make the garment pop or vanish at distance."));
        }

        var skinned = drawable.Lods.Values.SelectMany(l => l.Skinned).ToList();
        if (skinned.Count > 0 && skinned.All(s => !s))
            findings.Add(new Finding(Severity.Error, "ydd.unskinned",
                "No mesh in this drawable is skinned. Clothing must be rigged to the freemode skeleton."));

        if (drawable.Materials.Count == 0)
            findings.Add(new Finding(Severity.Error, "ydd.nomaterial",
                "Drawable has no materials."));

        var expectedDiffuse = ClothingNames.DiffuseTexture(component, drawableIndex);
        var diffuseRefs = drawable.Materials
            .SelectMany(m => m.Textures)
            .Where(t => t.Parameter.Contains("Diffuse", StringComparison.OrdinalIgnoreCase))
            .Select(t => t.Name)
            .Distinct()
            .ToList();

        if (diffuseRefs.Count == 0)
            findings.Add(new Finding(Severity.Error, "ydd.nodiffuse",
                "No material references a diffuse texture."));
        else if (!diffuseRefs.Contains(expectedDiffuse, StringComparer.Ordinal))
            findings.Add(new Finding(Severity.Warning, "ydd.diffusename",
                $"Material references diffuse '{string.Join(", ", diffuseRefs)}' but the target "
                + $"drawable index implies '{expectedDiffuse}'.",
                "The exported texture must be named to match what the material looks up."));

        var embeddedNames = drawable.EmbeddedTextures.Select(t => t.Name).ToList();
        if (embeddedNames.Count == 0)
            findings.Add(new Finding(Severity.Warning, "ydd.noembedded",
                "Drawable has no embedded textures. Freemode clothing normally embeds its "
                + "normal and specular maps."));

        return new ValidationReport(findings);
    }

    public ValidationReport ValidateAddonMetadata(PedMetadataInfo ymt, PedComponent component, int textureCount)
    {
        var findings = new List<Finding>();

        if (!string.Equals(ymt.Root, "CPedVariationInfo", StringComparison.Ordinal))
            findings.Add(new Finding(Severity.Error, "ymt.root",
                $"Metadata root is '{ymt.Root}', expected 'CPedVariationInfo'."));

        var slot = (int)component;
        if (ymt.AvailComp.Count != 12)
            findings.Add(new Finding(Severity.Error, "ymt.availcomp",
                $"availComp has {ymt.AvailComp.Count} entries, expected 12."));
        else
        {
            if (ymt.AvailComp[slot] == 255)
                findings.Add(new Finding(Severity.Error, "ymt.componentunavailable",
                    $"availComp marks component {slot} ({ClothingNames.Prefix(component)}) as unused."));

            var used = ymt.AvailComp.Where(v => v != 255).ToList();
            if (used.Count != ymt.Components.Count)
                findings.Add(new Finding(Severity.Error, "ymt.componentcount",
                    $"availComp lists {used.Count} available components but aComponentData3 has "
                    + $"{ymt.Components.Count} entries."));
        }

        var comp = ymt.Components.FirstOrDefault();
        if (comp is null)
            findings.Add(new Finding(Severity.Error, "ymt.nocomponent",
                "Metadata declares no component data."));
        else
        {
            if (comp.NumAvailTex != textureCount)
                findings.Add(new Finding(Severity.Error, "ymt.numavailtex",
                    $"numAvailTex is {comp.NumAvailTex} but {textureCount} texture(s) are being exported."));
            if (comp.DrawableCount != 1)
                findings.Add(new Finding(Severity.Warning, "ymt.drawablecount",
                    $"Pack declares {comp.DrawableCount} drawables; one per pack is the convention."));
        }

        return new ValidationReport(findings);
    }

    private static bool IsPowerOfTwo(int v) => v > 0 && (v & (v - 1)) == 0;
}

using Bitirim.Clothing.Core.Naming;

namespace Bitirim.Clothing.Export;

/// <summary>One garment inside a multi-garment resource.</summary>
public sealed record AddonGarment(
    string Name,
    PedComponent Component,
    int DrawableIndex,
    string SourceYddPath,
    string SourceYtdPath,
    IReadOnlyList<TextureSlot> Slots)
{
    /// <summary>
    /// The DLC identity for this garment inside the pack.
    /// </summary>
    /// <remarks>
    /// One addon DLC per garment, which is the structure the reference product
    /// ships (docs/video-feature-inventory.md 2.7: each exported trio carries
    /// its own <c>_&lt;component&gt;_&lt;index&gt;</c> DLC name and holds a
    /// single drawable at index 000). Packing several garments into one DLC
    /// would need the drawable indices inside the metadata to agree with the
    /// file names, which is a different and unproven layout.
    /// </remarks>
    public string DlcNameFor(string packDlcName) =>
        $"{packDlcName}_{ClothingNames.Prefix(Component)}_{DrawableIndex:D3}";
}

/// <summary>A whole resource: several garments, one manifest.</summary>
public sealed record MultiAddonExportRequest(
    string ResourceName,
    string Ped,
    string PackDlcName,
    string YmtTemplatePath,
    IReadOnlyList<AddonGarment> Garments,
    string OutputDirectory,
    ManifestMode ManifestMode = ManifestMode.Stream);

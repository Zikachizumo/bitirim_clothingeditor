using System.Runtime.Versioning;
using Bitirim.Clothing.Core.Naming;
using Bitirim.Clothing.Textures;

namespace Bitirim.Clothing.Export;

/// <param name="DesignPaths">
/// The painted textures, in texture-index order: element 0 is the colour the
/// shop shows on the garment's card.
/// </param>
public sealed record ShopImageRequest(
    string ResourceDirectory,
    PedComponent Component,
    int DrawableIndex,
    byte[] MeshBlob,
    IReadOnlyDictionary<string, int[]> ByteLayout,
    IReadOnlyList<string> DesignPaths);

public sealed record ShopImageResult(IReadOnlyList<string> Files, string? SkippedBecause);

/// <summary>
/// Draws the pictures the clothing shop puts on its cards.
/// </summary>
/// <remarks>
/// <para>
/// The shop does not render garments live; it shows PNGs prepared beforehand,
/// <c>images/{slot}_{drawable}.png</c> for the card and
/// <c>images/tex/{slot}_{drawable}_{texture}.png</c> for each colour swatch. So
/// re-skinning a garment without redrawing these leaves the shop advertising
/// the colours it used to have, on a garment that now looks different.
/// </para>
/// <para>
/// They go into <c>shop-images/</c> inside the exported resource, laid out the
/// way the shop's own <c>web/</c> folder is, so installing them is a copy
/// rather than a sorting job. They are deliberately not written into
/// <c>stream/</c>: they are not game assets and the streamer should never see
/// them.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public static class ShopImageWriter
{
    /// <summary>Matches the sizes the shop's existing images were drawn at.</summary>
    private const int CardSize = 256;
    private const int SwatchSize = 80;

    public const string FolderName = "shop-images";

    public static ShopImageResult Write(ShopImageRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var slot = ClothingNames.ShopSlot(request.Component);
        if (slot is null)
        {
            return new ShopImageResult(
                Array.Empty<string>(),
                $"The shop has no category for {ClothingNames.Prefix(request.Component)}.");
        }

        if (request.DesignPaths.Count == 0)
            return new ShopImageResult(Array.Empty<string>(), "No colours to draw.");

        var geometry = MeshThumbnailRenderer.ReadBlob(request.MeshBlob, request.ByteLayout);
        if (geometry.Positions.Length < 9 || geometry.Indices.Length < 3)
            return new ShopImageResult(Array.Empty<string>(), "The drawable carried no geometry.");

        var root = Path.Combine(request.ResourceDirectory, FolderName);
        var swatchDirectory = Path.Combine(root, "tex");
        Directory.CreateDirectory(swatchDirectory);

        var written = new List<string>();

        for (var index = 0; index < request.DesignPaths.Count; index++)
        {
            var surface = MeshThumbnailRenderer.Surface.FromFile(request.DesignPaths[index]);
            if (surface is null) continue;

            // The card shows the first colour, the same one the shop lands on
            // when a garment is selected.
            if (index == 0)
            {
                var card = Path.Combine(root, $"{slot}_{request.DrawableIndex}.png");
                if (MeshThumbnailRenderer.Render(geometry, card, CardSize, surface))
                    written.Add(card);
            }

            var swatch = Path.Combine(
                swatchDirectory, $"{slot}_{request.DrawableIndex}_{index}.png");
            if (MeshThumbnailRenderer.Render(geometry, swatch, SwatchSize, surface))
                written.Add(swatch);
        }

        return written.Count == 0
            ? new ShopImageResult(written, "Nothing rendered; the designs could not be read.")
            : new ShopImageResult(written, null);
    }
}

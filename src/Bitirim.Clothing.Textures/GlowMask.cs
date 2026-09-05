using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Bitirim.Clothing.Textures;

/// <summary>
/// Folds a garment's glow mask into the alpha channel of the texture that
/// ships to the game.
/// </summary>
/// <remarks>
/// <para>
/// A glowing garment's brightness is the product of three things, measured in
/// a running client: the mesh's vertex-colour blue channel, the diffuse alpha,
/// and the shader's emissive multiplier. The mesh half is the backend's job
/// (<c>MakeEmissiveAsync</c>); this is the alpha half, and it is the one that
/// decides the <em>shape</em> of the glow. Vertices are far too coarse for
/// that -- a whole jbib drawable carries about a thousand of them -- so a
/// lightning bolt painted on the mask lights up as a lightning bolt only
/// because alpha is per-pixel.
/// </para>
/// <para>
/// The design keeps its own colour untouched. The mask is a separate image so
/// that the shop thumbnail, which renders the design, is not punched full of
/// holes by an alpha channel that means something else entirely.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public static class GlowMask
{
    /// <summary>
    /// Writes <paramref name="designPath"/>'s colour with
    /// <paramref name="maskPath"/>'s luminance as alpha, and returns the path
    /// written to.
    /// </summary>
    /// <param name="maskPath">
    /// The mask, or null to make the whole garment glow evenly.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// The mask is a different size from the design. Resizing it here would
    /// blur a mask the user drew at pixel precision, so this refuses instead.
    /// </exception>
    public static string Apply(string designPath, string? maskPath, string destinationPath)
    {
        using var design = new Bitmap(designPath);
        using var mask = maskPath is null ? null : new Bitmap(maskPath);

        if (mask is not null && (mask.Width != design.Width || mask.Height != design.Height))
            throw new InvalidOperationException(
                $"The glow mask is {mask.Width}x{mask.Height} but the design is "
                + $"{design.Width}x{design.Height}.");

        using var output = new Bitmap(design.Width, design.Height, PixelFormat.Format32bppArgb);

        var area = new Rectangle(0, 0, design.Width, design.Height);
        var source = design.LockBits(area, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var target = output.LockBits(area, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        var overlay = mask?.LockBits(area, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

        try
        {
            var row = new byte[source.Stride];
            var maskRow = overlay is null ? null : new byte[overlay.Stride];

            for (var y = 0; y < design.Height; y++)
            {
                Marshal.Copy(source.Scan0 + y * source.Stride, row, 0, row.Length);
                if (overlay is not null)
                    Marshal.Copy(overlay.Scan0 + y * overlay.Stride, maskRow!, 0, maskRow!.Length);

                for (var x = 0; x < design.Width; x++)
                {
                    var i = x * 4;

                    if (maskRow is null)
                    {
                        row[i + 3] = 255;
                        continue;
                    }

                    // Rec. 601 luma, and scaled by the mask's own alpha so an
                    // unpainted (transparent) area reads as "does not glow"
                    // rather than as black-that-happens-to-be-there.
                    var luma = (maskRow[i + 2] * 299 + maskRow[i + 1] * 587 + maskRow[i] * 114) / 1000;
                    row[i + 3] = (byte)(luma * maskRow[i + 3] / 255);
                }

                Marshal.Copy(row, 0, target.Scan0 + y * target.Stride, row.Length);
            }
        }
        finally
        {
            design.UnlockBits(source);
            output.UnlockBits(target);
            if (overlay is not null) mask!.UnlockBits(overlay);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        output.Save(destinationPath, ImageFormat.Png);
        return destinationPath;
    }
}

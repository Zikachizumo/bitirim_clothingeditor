using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using BCnEncoder.Decoder;

namespace Bitirim.Clothing.Textures;

/// <summary>
/// Decodes a DDS surface to PNG so the texture editor can start from the
/// garment's existing diffuse rather than a blank canvas.
/// </summary>
/// <remarks>
/// The asset backend hands us the texture as DDS; neither the browser nor
/// System.Drawing can read that. BCnEncoder.NET does the block decompression
/// and System.Drawing writes the PNG.
/// </remarks>
[SupportedOSPlatform("windows")]
public static class DdsImageDecoder
{
    /// <summary>Decodes the top mip of a DDS file and writes it as a PNG.</summary>
    /// <returns>The PNG's dimensions.</returns>
    public static (int Width, int Height) DdsToPng(string ddsPath, string pngPath)
    {
        using var input = File.OpenRead(ddsPath);

        var decoder = new BcDecoder();
        var pixels = decoder.Decode2D(input).Span;

        var height = pixels.Height;
        var width = pixels.Width;
        if (width <= 0 || height <= 0)
            throw new InvalidOperationException("The texture decoded to an empty image.");

        using var bitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var data = bitmap.LockBits(
            new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            var stride = data.Stride;
            var row = new byte[stride];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var p = pixels[y, x];
                    var i = x * 4;
                    // System.Drawing expects BGRA in memory.
                    row[i + 0] = p.b;
                    row[i + 1] = p.g;
                    row[i + 2] = p.r;
                    row[i + 3] = p.a;
                }
                Marshal.Copy(row, 0, data.Scan0 + y * stride, stride);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(pngPath)!);
        bitmap.Save(pngPath, ImageFormat.Png);
        return (width, height);
    }
}

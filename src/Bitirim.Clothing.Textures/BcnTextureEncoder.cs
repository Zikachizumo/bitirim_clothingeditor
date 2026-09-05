using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using BCnEncoder.Encoder;
using BCnEncoder.Shared;
using Bitirim.Clothing.Core.Rage;
using Bitirim.Clothing.Core.Textures;

namespace Bitirim.Clothing.Textures;

/// <summary>
/// Block compression via BCnEncoder.NET (Unlicense, no native dependencies).
/// </summary>
/// <remarks>
/// Surfaces are emitted as a single buffer holding every mip level back to
/// back, largest first, which is the layout the texture dictionary stores.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class BcnTextureEncoder : ITextureEncoder
{
    private static readonly Dictionary<string, CompressionFormat> Formats = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BC1"] = CompressionFormat.Bc1,
        ["BC3"] = CompressionFormat.Bc3,
        ["BC4"] = CompressionFormat.Bc4,
        ["BC5"] = CompressionFormat.Bc5,
        ["BC7"] = CompressionFormat.Bc7,
    };

    public IReadOnlyList<string> SupportedFormats { get; } = Formats.Keys.ToList();

    public EncodedTexture EncodeFile(string imagePath, string format, bool generateMips = true)
    {
        var (rgba, w, h) = LoadRgba(imagePath);
        return EncodeRgba(rgba, w, h, format, generateMips);
    }

    public EncodedTexture EncodeRgba(
        ReadOnlySpan<byte> rgba, int width, int height, string format, bool generateMips = true)
    {
        if (!Formats.TryGetValue(format, out var compression))
            throw new ArgumentException($"Unsupported texture format: {format}", nameof(format));
        if (width <= 0 || height <= 0)
            throw new ArgumentException("Texture dimensions must be positive.");
        if (!IsPowerOfTwo(width) || !IsPowerOfTwo(height))
            throw new ArgumentException(
                $"GTA clothing textures must be power-of-two; got {width}x{height}.");

        var encoder = new BcEncoder
        {
            OutputOptions =
            {
                Format = compression,
                Quality = CompressionQuality.Balanced,
                GenerateMipMaps = generateMips,
                MaxMipMapLevel = generateMips ? MipLevelsTo4x4(width, height) : 1,
            },
        };

        var levels = new List<byte[]>();
        var pixels = ToColorRgba32(rgba, width, height);
        var mips = encoder.EncodeToRawBytes(pixels);
        foreach (var level in mips) levels.Add(level);

        var total = levels.Sum(l => l.Length);
        var buffer = new byte[total];
        var offset = 0;
        foreach (var level in levels)
        {
            Buffer.BlockCopy(level, 0, buffer, offset, level.Length);
            offset += level.Length;
        }

        return new EncodedTexture(buffer, width, height, format.ToUpperInvariant(), levels.Count);
    }

    private static ColorRgba32[,] ToColorRgba32(ReadOnlySpan<byte> rgba, int width, int height)
    {
        var expected = (long)width * height * 4;
        if (rgba.Length < expected)
            throw new ArgumentException(
                $"Pixel buffer is {rgba.Length} bytes; {expected} required for {width}x{height} RGBA.");

        var pixels = new ColorRgba32[height, width];
        for (var y = 0; y < height; y++)
        {
            var row = y * width * 4;
            for (var x = 0; x < width; x++)
            {
                var i = row + x * 4;
                pixels[y, x] = new ColorRgba32(rgba[i], rgba[i + 1], rgba[i + 2], rgba[i + 3]);
            }
        }

        return pixels;
    }

    /// <summary>
    /// Decodes an image to straight 8-bit RGBA.
    /// </summary>
    /// <remarks>
    /// System.Drawing gives BGRA in memory, so channels are swapped on the way
    /// out. Windows-only, which matches the product's target platform.
    /// </remarks>
    private static (byte[] Rgba, int Width, int Height) LoadRgba(string path)
    {
        using var bitmap = new Bitmap(path);
        var width = bitmap.Width;
        var height = bitmap.Height;

        using var argb = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(argb))
        {
            g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
            g.DrawImage(bitmap, 0, 0, width, height);
        }

        var data = argb.LockBits(
            new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            var rgba = new byte[width * height * 4];
            var scan = data.Scan0;
            var stride = data.Stride;
            var row = new byte[stride];

            for (var y = 0; y < height; y++)
            {
                Marshal.Copy(scan + y * stride, row, 0, stride);
                for (var x = 0; x < width; x++)
                {
                    var s = x * 4;
                    var d = (y * width + x) * 4;
                    rgba[d + 0] = row[s + 2]; // R <- B
                    rgba[d + 1] = row[s + 1]; // G
                    rgba[d + 2] = row[s + 0]; // B <- R
                    rgba[d + 3] = row[s + 3]; // A
                }
            }

            return (rgba, width, height);
        }
        finally
        {
            argb.UnlockBits(data);
        }
    }

    /// <summary>
    /// Number of mip levels down to 4x4 inclusive.
    /// </summary>
    /// <remarks>
    /// BC formats compress in 4x4 blocks, so 2x2 and 1x1 levels still occupy a
    /// whole block and carry no extra detail. The game's own clothing textures
    /// stop at 4x4 -- jbib_diff_000_a_uni is 512x512 with 8 levels, not 10 --
    /// and we match that rather than shipping two redundant levels.
    /// </remarks>
    private static int MipLevelsTo4x4(int width, int height)
    {
        var smallest = Math.Min(width, height);
        var levels = 1;
        while (smallest > 4)
        {
            smallest >>= 1;
            levels++;
        }
        return levels;
    }

    private static bool IsPowerOfTwo(int v) => v > 0 && (v & (v - 1)) == 0;
}

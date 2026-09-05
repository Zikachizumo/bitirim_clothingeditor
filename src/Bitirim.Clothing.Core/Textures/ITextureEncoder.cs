using Bitirim.Clothing.Core.Rage;

namespace Bitirim.Clothing.Core.Textures;

/// <summary>
/// Turns an RGBA image into a block-compressed surface with a full mip chain.
/// </summary>
/// <remarks>
/// This lives host-side on purpose. The asset backend stores compressed
/// surfaces but does not produce them, so compression is ours to own; that also
/// keeps the expensive part in a language where we can profile and parallelise
/// it properly. See docs/phase-1-results.md section 5.
/// </remarks>
public interface ITextureEncoder
{
    /// <summary>Formats this encoder can produce, e.g. BC1, BC3, BC7.</summary>
    IReadOnlyList<string> SupportedFormats { get; }

    /// <summary>
    /// Encodes an image file (PNG/JPG/etc.) to the requested block format.
    /// </summary>
    /// <param name="generateMips">
    /// GTA clothing textures ship with a full mip chain; omitting it causes
    /// visible shimmer at distance.
    /// </param>
    EncodedTexture EncodeFile(string imagePath, string format, bool generateMips = true);

    /// <summary>Encodes raw 8-bit RGBA, row-major, no padding.</summary>
    EncodedTexture EncodeRgba(
        ReadOnlySpan<byte> rgba, int width, int height, string format, bool generateMips = true);
}

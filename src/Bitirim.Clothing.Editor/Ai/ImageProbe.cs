using System.Buffers.Binary;

namespace Bitirim.Clothing.Editor.Ai;

/// <summary>
/// Reads an image's pixel dimensions from its header bytes.
/// </summary>
/// <remarks>
/// Enough to report what came back and to tell a 2K result from a 1K one. A
/// full decoder is not needed: nothing here looks at pixels, and the generated
/// file is written to disk untouched so that what the model produced is exactly
/// what the layer draws.
/// </remarks>
public static class ImageProbe
{
    /// <summary>The file extension for a mime type we accept, or null.</summary>
    public static string? ExtensionFor(string? mimeType) => mimeType?.ToLowerInvariant() switch
    {
        "image/png" => ".png",
        "image/jpeg" or "image/jpg" => ".jpg",
        "image/webp" => ".webp",
        _ => null,
    };

    public static (int Width, int Height) Measure(byte[] data)
    {
        if (TryPng(data, out var png)) return png;
        if (TryJpeg(data, out var jpeg)) return jpeg;
        if (TryWebp(data, out var webp)) return webp;
        return (0, 0);
    }

    private static bool TryPng(byte[] d, out (int, int) size)
    {
        size = (0, 0);
        // 8-byte signature, then a 4-byte length, "IHDR", width, height.
        if (d.Length < 24) return false;
        if (d[0] != 0x89 || d[1] != 'P' || d[2] != 'N' || d[3] != 'G') return false;
        if (d[12] != 'I' || d[13] != 'H' || d[14] != 'D' || d[15] != 'R') return false;

        size = (BinaryPrimitives.ReadInt32BigEndian(d.AsSpan(16, 4)),
                BinaryPrimitives.ReadInt32BigEndian(d.AsSpan(20, 4)));
        return true;
    }

    private static bool TryJpeg(byte[] d, out (int, int) size)
    {
        size = (0, 0);
        if (d.Length < 4 || d[0] != 0xFF || d[1] != 0xD8) return false;

        var i = 2;
        while (i + 9 < d.Length)
        {
            if (d[i] != 0xFF) { i++; continue; }

            var marker = d[i + 1];
            // Standalone markers carry no length.
            if (marker is 0xD8 or 0x01 || (marker >= 0xD0 && marker <= 0xD7)) { i += 2; continue; }

            var length = BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(i + 2, 2));

            // Any SOFn except the arithmetic-coding and DHT/DAC ones carries the
            // frame size at a fixed offset.
            var isFrameHeader = marker is >= 0xC0 and <= 0xCF
                                && marker is not (0xC4 or 0xC8 or 0xCC);
            if (isFrameHeader)
            {
                size = (BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(i + 7, 2)),
                        BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(i + 5, 2)));
                return true;
            }

            i += 2 + length;
        }

        return false;
    }

    private static bool TryWebp(byte[] d, out (int, int) size)
    {
        size = (0, 0);
        if (d.Length < 30) return false;
        if (d[0] != 'R' || d[1] != 'I' || d[2] != 'F' || d[3] != 'F') return false;
        if (d[8] != 'W' || d[9] != 'E' || d[10] != 'B' || d[11] != 'P') return false;

        var fourcc = System.Text.Encoding.ASCII.GetString(d, 12, 4);
        switch (fourcc)
        {
            case "VP8 ":
                size = (BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(26, 2)) & 0x3FFF,
                        BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(28, 2)) & 0x3FFF);
                return true;

            case "VP8L":
            {
                var bits = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(21, 4));
                size = ((int)(bits & 0x3FFF) + 1, (int)((bits >> 14) & 0x3FFF) + 1);
                return true;
            }

            case "VP8X":
                size = (1 + (d[24] | (d[25] << 8) | (d[26] << 16)),
                        1 + (d[27] | (d[28] << 8) | (d[29] << 16)));
                return true;

            default:
                return false;
        }
    }
}

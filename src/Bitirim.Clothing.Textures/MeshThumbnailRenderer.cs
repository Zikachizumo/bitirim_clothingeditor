using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Versioning;

namespace Bitirim.Clothing.Textures;

/// <summary>
/// Renders a drawable's geometry to a small PNG.
/// </summary>
/// <remarks>
/// This is a real render of the real mesh -- a z-buffered rasteriser over the
/// vertex data the asset backend extracted -- not a generic icon and not a
/// picture of the texture. A card that shows a garment's actual silhouette is
/// the whole point of a thumbnail; anything else would be decoration.
///
/// A software rasteriser is used rather than a GPU context because thumbnails
/// are generated in the background, off the UI thread, in a WPF process that
/// has no spare swap chain to render into. At 256x256 with the low LOD of a
/// clothing drawable this costs a few milliseconds.
///
/// The camera is orthographic and faces the garment front-on, so two drawables
/// of the same component are directly comparable in the grid.
/// </remarks>
[SupportedOSPlatform("windows")]
public static class MeshThumbnailRenderer
{
    /// <summary>Vertex streams as laid out in the backend's blob.</summary>
    public sealed record Geometry(
        float[] Positions, float[]? Normals, uint[] Indices, float[]? Uvs = null);

    /// <summary>A decoded image the rasteriser can sample, BGRA top-down.</summary>
    public sealed record Surface(byte[] Bgra, int Width, int Height)
    {
        /// <summary>Loads a PNG (or any format GDI+ reads) for sampling.</summary>
        public static Surface? FromFile(string path)
        {
            if (!File.Exists(path)) return null;
            using var bitmap = new Bitmap(path);
            var size = bitmap.Width * bitmap.Height * 4;
            var pixels = new byte[size];

            var data = bitmap.LockBits(
                new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                ImageLockMode.ReadOnly,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                for (var y = 0; y < bitmap.Height; y++)
                {
                    System.Runtime.InteropServices.Marshal.Copy(
                        data.Scan0 + y * data.Stride, pixels, y * bitmap.Width * 4, bitmap.Width * 4);
                }
            }
            finally
            {
                bitmap.UnlockBits(data);
            }

            return new Surface(pixels, bitmap.Width, bitmap.Height);
        }

        /// <summary>Nearest-neighbour sample; UVs wrap, as the game's do.</summary>
        public void Sample(float u, float v, out byte b, out byte g, out byte r)
        {
            if (!float.IsFinite(u)) u = 0f;
            if (!float.IsFinite(v)) v = 0f;

            var x = (int)((u - MathF.Floor(u)) * Width);
            var y = (int)((v - MathF.Floor(v)) * Height);
            x = Math.Clamp(x, 0, Width - 1);
            y = Math.Clamp(y, 0, Height - 1);

            var offset = (y * Width + x) * 4;
            b = Bgra[offset];
            g = Bgra[offset + 1];
            r = Bgra[offset + 2];
        }
    }

    /// <summary>
    /// Reads the backend's mesh blob using its byte-offset map.
    /// </summary>
    /// <param name="layout">
    /// Stream name to <c>[offset, length]</c> in bytes, exactly as
    /// <c>MeshInfo.ByteLayout</c> reports it.
    /// </param>
    public static Geometry ReadBlob(byte[] blob, IReadOnlyDictionary<string, int[]> layout)
    {
        float[] Floats(string key)
        {
            if (!layout.TryGetValue(key, out var span) || span.Length < 2) return Array.Empty<float>();
            var (offset, length) = (span[0], span[1]);
            if (offset < 0 || length <= 0 || offset + length > blob.Length) return Array.Empty<float>();

            var result = new float[length / sizeof(float)];
            Buffer.BlockCopy(blob, offset, result, 0, result.Length * sizeof(float));
            return result;
        }

        uint[] Indices()
        {
            if (!layout.TryGetValue("indices", out var span) || span.Length < 2) return Array.Empty<uint>();
            var (offset, length) = (span[0], span[1]);
            if (offset < 0 || length <= 0 || offset + length > blob.Length) return Array.Empty<uint>();

            var result = new uint[length / sizeof(uint)];
            Buffer.BlockCopy(blob, offset, result, 0, result.Length * sizeof(uint));
            return result;
        }

        var normals = Floats("normals");
        var uvs = Floats("uvs");
        return new Geometry(
            Floats("positions"),
            normals.Length > 0 ? normals : null,
            Indices(),
            uvs.Length > 0 ? uvs : null);
    }

    /// <summary>
    /// Rasterises the geometry and writes a PNG.
    /// </summary>
    /// <returns>False when there is nothing renderable, so the caller can show
    /// a placeholder rather than an empty square presented as a render.</returns>
    /// <param name="texture">
    /// Optional diffuse to sample per pixel. Without one the render is a shaded
    /// silhouette, which is what a library card wants; with one it is what the
    /// clothing shop shows, so the same rasteriser serves both.
    /// </param>
    public static bool Render(
        Geometry geometry, string destinationPng, int size = 256, Surface? texture = null)
    {
        var (positions, normals, indices) = (geometry.Positions, geometry.Normals, geometry.Indices);
        if (positions.Length < 9 || indices.Length < 3) return false;

        var uvs = geometry.Uvs;
        if (texture is not null && (uvs is null || uvs.Length < positions.Length / 3 * 2))
        {
            // No UVs means nothing to sample against; fall back to the
            // silhouette rather than painting the garment a flat wrong colour.
            texture = null;
        }

        var vertexCount = positions.Length / 3;

        // ------------------------------------------------------------------
        // Fit the model to the frame.
        //
        // RAGE is Z-up; the thumbnail is drawn in screen space with Y down, so
        // the model's Z becomes the vertical axis and its Y becomes depth. This
        // is the same axis convention the viewport bakes into its geometry.
        // ------------------------------------------------------------------
        float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;

        for (var i = 0; i < vertexCount; i++)
        {
            var x = positions[i * 3];
            var y = positions[i * 3 + 1];
            var z = positions[i * 3 + 2];
            if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z)) continue;
            if (x < minX) minX = x;
            if (y < minY) minY = y;
            if (z < minZ) minZ = z;
            if (x > maxX) maxX = x;
            if (y > maxY) maxY = y;
            if (z > maxZ) maxZ = z;
        }

        if (minX > maxX) return false;

        var centreX = (minX + maxX) / 2f;
        var centreY = (minY + maxY) / 2f;
        var centreZ = (minZ + maxZ) / 2f;

        var extent = Math.Max(maxX - minX, maxZ - minZ);
        if (extent <= 0f) return false;

        var margin = size * 0.10f;
        var scale = (size - margin * 2f) / (extent * 1.05f);

        var depth = new float[size * size];
        Array.Fill(depth, float.MaxValue);

        var colour = new byte[size * size * 4]; // BGRA, premultiplied by nothing

        // A single key light from the front-upper-left plus a weak fill, which
        // is enough to read a garment's folds without inventing a material.
        var keyDirection = Normalise(-0.45f, -0.75f, -0.5f);
        var fillDirection = Normalise(0.5f, 0.35f, 0.4f);

        var triangles = indices.Length / 3;
        for (var t = 0; t < triangles; t++)
        {
            var i0 = indices[t * 3];
            var i1 = indices[t * 3 + 1];
            var i2 = indices[t * 3 + 2];
            if (i0 >= vertexCount || i1 >= vertexCount || i2 >= vertexCount) continue;

            Project(positions, i0, centreX, centreZ, centreY, scale, size, out var ax, out var ay, out var az);
            Project(positions, i1, centreX, centreZ, centreY, scale, size, out var bx, out var by, out var bz);
            Project(positions, i2, centreX, centreZ, centreY, scale, size, out var cx, out var cy, out var cz);

            float nx, ny, nz;
            if (normals is not null && normals.Length >= vertexCount * 3)
            {
                nx = (normals[i0 * 3] + normals[i1 * 3] + normals[i2 * 3]) / 3f;
                ny = (normals[i0 * 3 + 1] + normals[i1 * 3 + 1] + normals[i2 * 3 + 1]) / 3f;
                nz = (normals[i0 * 3 + 2] + normals[i1 * 3 + 2] + normals[i2 * 3 + 2]) / 3f;
            }
            else
            {
                // No normals in the stream: derive one from the face itself.
                FaceNormal(positions, i0, i1, i2, out nx, out ny, out nz);
            }

            var length = MathF.Sqrt(nx * nx + ny * ny + nz * nz);
            if (length > 1e-6f) { nx /= length; ny /= length; nz /= length; }

            var lambert = MathF.Max(0f, Dot(nx, ny, nz, keyDirection))
                          * 0.72f
                          + MathF.Max(0f, Dot(nx, ny, nz, fillDirection)) * 0.22f
                          + 0.16f;
            var shade = (byte)Math.Clamp(lambert * 255f, 24f, 255f);

            RasteriseTriangle(
                ax, ay, az, bx, by, bz, cx, cy, cz,
                shade, size, depth, colour,
                texture, uvs, i0, i1, i2);
        }

        var covered = 0;
        for (var i = 3; i < colour.Length; i += 4) if (colour[i] != 0) covered++;
        if (covered < 16) return false; // nothing meaningful landed on the canvas

        WritePng(colour, size, destinationPng);
        return true;
    }

    // ----------------------------------------------------------------------

    private static void Project(
        float[] positions, uint index,
        float centreX, float centreZ, float centreDepth,
        float scale, int size,
        out float x, out float y, out float z)
    {
        var px = positions[index * 3];
        var py = positions[index * 3 + 1];
        var pz = positions[index * 3 + 2];

        x = (px - centreX) * scale + size / 2f;
        y = size / 2f - (pz - centreZ) * scale;   // Z is up in RAGE, down on screen
        z = (py - centreDepth) * scale;           // Y is depth once Z is vertical
    }

    private static void FaceNormal(
        float[] p, uint i0, uint i1, uint i2,
        out float nx, out float ny, out float nz)
    {
        float ax = p[i1 * 3] - p[i0 * 3], ay = p[i1 * 3 + 1] - p[i0 * 3 + 1], az = p[i1 * 3 + 2] - p[i0 * 3 + 2];
        float bx = p[i2 * 3] - p[i0 * 3], by = p[i2 * 3 + 1] - p[i0 * 3 + 1], bz = p[i2 * 3 + 2] - p[i0 * 3 + 2];
        nx = ay * bz - az * by;
        ny = az * bx - ax * bz;
        nz = ax * by - ay * bx;
    }

    private static (float X, float Y, float Z) Normalise(float x, float y, float z)
    {
        var l = MathF.Sqrt(x * x + y * y + z * z);
        return l < 1e-6f ? (0f, 0f, 1f) : (x / l, y / l, z / l);
    }

    private static float Dot(float x, float y, float z, (float X, float Y, float Z) d) =>
        x * d.X + y * d.Y + z * d.Z;

    private static void RasteriseTriangle(
        float ax, float ay, float az,
        float bx, float by, float bz,
        float cx, float cy, float cz,
        byte shade, int size, float[] depth, byte[] colour,
        Surface? texture, float[]? uvs, uint i0, uint i1, uint i2)
    {
        var minX = (int)MathF.Floor(Math.Min(ax, Math.Min(bx, cx)));
        var maxX = (int)MathF.Ceiling(Math.Max(ax, Math.Max(bx, cx)));
        var minY = (int)MathF.Floor(Math.Min(ay, Math.Min(by, cy)));
        var maxY = (int)MathF.Ceiling(Math.Max(ay, Math.Max(by, cy)));

        if (maxX < 0 || maxY < 0 || minX >= size || minY >= size) return;

        minX = Math.Max(minX, 0);
        minY = Math.Max(minY, 0);
        maxX = Math.Min(maxX, size - 1);
        maxY = Math.Min(maxY, size - 1);

        var area = (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
        if (MathF.Abs(area) < 1e-8f) return;
        var inverseArea = 1f / area;

        for (var y = minY; y <= maxY; y++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                var px = x + 0.5f;
                var py = y + 0.5f;

                var w0 = ((bx - px) * (cy - py) - (by - py) * (cx - px)) * inverseArea;
                var w1 = ((cx - px) * (ay - py) - (cy - py) * (ax - px)) * inverseArea;
                var w2 = 1f - w0 - w1;

                if (w0 < 0f || w1 < 0f || w2 < 0f) continue;

                var z = w0 * az + w1 * bz + w2 * cz;
                var slot = y * size + x;
                if (z >= depth[slot]) continue;

                depth[slot] = z;
                var o = slot * 4;

                if (texture is null || uvs is null)
                {
                    colour[o] = shade;      // B
                    colour[o + 1] = shade;  // G
                    colour[o + 2] = shade;  // R
                    colour[o + 3] = 255;    // A
                    continue;
                }

                // Barycentric interpolation of the UVs, then the lighting term
                // multiplies the sampled colour -- so folds still read on a
                // flat-coloured garment.
                var u = w0 * uvs[i0 * 2] + w1 * uvs[i1 * 2] + w2 * uvs[i2 * 2];
                var v = w0 * uvs[i0 * 2 + 1] + w1 * uvs[i1 * 2 + 1] + w2 * uvs[i2 * 2 + 1];
                texture.Sample(u, v, out var tb, out var tg, out var tr);

                var light = shade / 255f;
                colour[o] = (byte)Math.Clamp(tb * light, 0f, 255f);
                colour[o + 1] = (byte)Math.Clamp(tg * light, 0f, 255f);
                colour[o + 2] = (byte)Math.Clamp(tr * light, 0f, 255f);
                colour[o + 3] = 255;
            }
        }
    }

    private static void WritePng(byte[] bgra, int size, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

        using var bitmap = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var data = bitmap.LockBits(
            new Rectangle(0, 0, size, size),
            ImageLockMode.WriteOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);

        try
        {
            for (var y = 0; y < size; y++)
            {
                System.Runtime.InteropServices.Marshal.Copy(
                    bgra, y * size * 4, data.Scan0 + y * data.Stride, size * 4);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        var temp = destination + ".tmp";
        bitmap.Save(temp, ImageFormat.Png);
        File.Move(temp, destination, overwrite: true);
    }
}

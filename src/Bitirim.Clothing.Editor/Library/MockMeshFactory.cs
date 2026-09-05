namespace Bitirim.Clothing.Editor.Library;

public sealed record MeshPayload(
    float[] Positions,
    float[] Normals,
    float[] Uvs,
    uint[] Indices,
    int VertexCount,
    int TriangleCount,
    bool IsMock,
    string Source);

/// <summary>
/// Synthetic geometry, so the editor is usable without a GTA installation.
/// </summary>
/// <remarks>
/// This exists purely so viewport, UV and texture work is not blocked on having
/// game assets. Anything built on it carries a MOCK ASSET badge through the UI
/// and must never be described as a real GTA drawable. It is also never
/// exportable as a FiveM resource -- there is no real drawable to ship.
/// </remarks>
public static class MockMeshFactory
{
    /// <summary>
    /// A torso-shaped shell: a tapered, rounded box roughly the proportions of
    /// a freemode jacket, with a UV layout of front/back panels.
    /// </summary>
    public static MeshPayload Torso(int segments = 24, int rings = 16)
    {
        var positions = new List<float>();
        var normals = new List<float>();
        var uvs = new List<float>();
        var indices = new List<uint>();

        // Ped-space-ish: chest sits around z 0.9 .. 1.55, matching where a real
        // jbib drawable lives, so camera framing carries over unchanged.
        const float zBottom = 0.90f, zTop = 1.58f;

        for (var r = 0; r <= rings; r++)
        {
            var v = r / (float)rings;
            var z = zBottom + (zTop - zBottom) * v;

            // Waist in, chest out, shoulders in again.
            var taper = 0.78f + 0.30f * MathF.Sin(v * MathF.PI * 0.95f);
            var radiusX = 0.26f * taper;
            var radiusY = 0.15f * taper;

            for (var s = 0; s <= segments; s++)
            {
                var u = s / (float)segments;
                var a = u * MathF.Tau;

                var x = MathF.Cos(a) * radiusX;
                var y = MathF.Sin(a) * radiusY;
                positions.Add(x); positions.Add(y); positions.Add(z);

                var n = MathF.Sqrt(x * x + y * y);
                normals.Add(n > 0 ? x / n : 0f);
                normals.Add(n > 0 ? y / n : 0f);
                normals.Add(0f);

                // Front panel on the left half of the texture, back on the right.
                uvs.Add(u);
                uvs.Add(1f - v);
            }
        }

        var stride = segments + 1;
        for (var r = 0; r < rings; r++)
        {
            for (var s = 0; s < segments; s++)
            {
                var a = (uint)(r * stride + s);
                var b = (uint)(a + 1);
                var c = (uint)((r + 1) * stride + s);
                var d = (uint)(c + 1);
                indices.Add(a); indices.Add(c); indices.Add(b);
                indices.Add(b); indices.Add(c); indices.Add(d);
            }
        }

        return new MeshPayload(
            positions.ToArray(), normals.ToArray(), uvs.ToArray(), indices.ToArray(),
            positions.Count / 3, indices.Count / 3,
            IsMock: true,
            Source: "Synthetic torso shell");
    }
}

using System.Text.Json.Serialization;

namespace Bitirim.Clothing.Core.Rage;

public sealed record BackendCapabilities(
    string ContractVersion,
    string Backend,
    string BackendVersion,
    string Python,
    IReadOnlyList<string> Operations,
    bool ReadYdd,
    bool ReadYtd,
    bool ReadYmt,
    bool WriteYtd,
    bool WriteYmt,
    bool WriteYdd,
    bool WriteYddGeometry,
    bool EncodeTexture);

public sealed record TextureInfo(
    string Name,
    int Width,
    int Height,
    string Format,
    int MipCount,
    int Usage,
    int UsageFlags,
    long DataBytes,
    string DataSha);

public sealed record TextureDictionaryInfo(
    string Game,
    int TextureCount,
    IReadOnlyList<TextureInfo> Textures,
    double ElapsedMs);

public sealed record MaterialTextureRef(string Parameter, string Name);

public sealed record MaterialInfo(
    string Shader,
    string ShaderFile,
    int RenderBucket,
    IReadOnlyList<MaterialTextureRef> Textures);

public sealed record LodInfo(
    int Meshes,
    int Vertices,
    int Indices,
    IReadOnlyList<int> UvSets,
    IReadOnlyList<bool> Skinned,
    int BlendIndices,
    int BlendWeights,
    int Normals,
    int Tangents);

public sealed record BoundingInfo(
    IReadOnlyList<double> Min,
    IReadOnlyList<double> Max,
    double Radius);

public sealed record DrawableInfo(
    string WrapperName,
    long NameHash,
    BoundingInfo BoundingBox,
    bool HasSkeleton,
    bool HasJoints,
    IReadOnlyList<MaterialInfo> Materials,
    IReadOnlyList<string> TextureNames,
    IReadOnlyList<TextureInfo> EmbeddedTextures,
    IReadOnlyDictionary<string, LodInfo> Lods);

public sealed record DrawableDictionaryInfo(
    string Name,
    int Version,
    string Game,
    int DrawableCount,
    IReadOnlyList<DrawableInfo> Drawables,
    double ElapsedMs);

public sealed record PedDrawableVariationInfo(int Textures, bool OwnsCloth);

public sealed record PedComponentInfo(
    int Slot,
    int NumAvailTex,
    int DrawableCount,
    IReadOnlyList<PedDrawableVariationInfo> Drawables);

public sealed record PedMetadataInfo(
    string ContentType,
    string Format,
    int ResourceVersion,
    string? Root,
    long DlcName,
    IReadOnlyList<int> AvailComp,
    IReadOnlyDictionary<string, bool> Flags,
    IReadOnlyList<PedComponentInfo> Components,
    int CompInfoCount,
    double ElapsedMs);

public sealed record WriteResult(
    string Path,
    long SizeBytes,
    IReadOnlyList<string> Issues,
    double ElapsedMs);

public sealed record PackFileResult(string File, bool Ok, string? Kind, long SizeBytes, string? Error);

public sealed record PackValidationResult(bool AllOk, IReadOnlyList<PackFileResult> Files);

public sealed record MeshPart(
    int MaterialIndex,
    int IndexStart,
    int IndexCount,
    int VertexCount,
    bool Skinned,
    int UvSets,
    bool HasNormals);

public sealed record MeshMaterial(string Shader, IReadOnlyList<MaterialTextureRef> Textures);

/// <summary>
/// Render geometry for the viewport, written to a side file.
/// </summary>
/// <remarks>
/// The vertex data is not carried in this record: a garment can run to tens of
/// thousands of vertices, and moving that through a JSON envelope would cost far
/// more than writing the raw floats once. <see cref="ByteLayout"/> describes
/// where each stream sits inside the blob.
/// </remarks>
public sealed record MeshInfo(
    string Blob,
    int VertexCount,
    int IndexCount,
    int TriangleCount,
    string Lod,
    IReadOnlyList<MeshPart> Parts,
    IReadOnlyList<MeshMaterial> Materials,
    BoundingInfo Bounds,
    IReadOnlyDictionary<string, int[]> ByteLayout,
    double ElapsedMs);

/// <summary>
/// Block-compressed surface data plus everything the container needs to
/// describe it. Produced host-side by <see cref="Textures.ITextureEncoder"/>.
/// </summary>
public sealed record EncodedTexture(
    byte[] Data,
    int Width,
    int Height,
    string Format,
    int MipCount)
{
    [JsonIgnore]
    public long SizeBytes => Data.LongLength;
}

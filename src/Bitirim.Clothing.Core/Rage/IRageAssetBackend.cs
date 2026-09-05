namespace Bitirim.Clothing.Core.Rage;

/// <summary>
/// The whole application's view of GTA V binary asset I/O.
/// </summary>
/// <remarks>
/// Nothing above this interface knows that the current implementation is a
/// Python process wrapping fivefury. That is deliberate: fivefury is pre-1.0
/// (docs/risks.md R2), and when it is replaced -- by a forked RageLib, or by
/// our own RSC7 packer -- only the implementation changes.
///
/// Implementations must never throw raw parser exceptions across this
/// boundary. Failures surface as <see cref="RageAssetException"/> with a
/// stable code the UI can map to a readable message.
/// </remarks>
public interface IRageAssetBackend : IAsyncDisposable
{
    /// <summary>
    /// What this backend can actually do. The UI binds affordances to these
    /// flags, so a capability we have not proven is structurally impossible to
    /// offer as a working feature.
    /// </summary>
    Task<BackendCapabilities> GetCapabilitiesAsync(CancellationToken ct = default);

    Task<DrawableDictionaryInfo> ReadYddAsync(string path, CancellationToken ct = default);

    Task<TextureDictionaryInfo> ReadYtdAsync(string path, CancellationToken ct = default);

    Task<PedMetadataInfo> ReadYmtAsync(string path, CancellationToken ct = default);

    /// <summary>
    /// Extracts render geometry for the viewport into <paramref name="blobPath"/>.
    /// </summary>
    /// <param name="lod">"high", "med" or "low".</param>
    Task<MeshInfo> ExtractMeshAsync(
        string path, string blobPath, string lod = "high", CancellationToken ct = default);

    /// <summary>
    /// Writes a texture out as DDS so the host can decode it for editing.
    /// </summary>
    Task<TextureInfo> DecodeTextureAsync(
        string path, string blobPath, int index = 0, CancellationToken ct = default);

    /// <summary>
    /// Writes a copy of <paramref name="sourcePath"/> with its first texture
    /// replaced by pre-encoded block data.
    /// </summary>
    /// <remarks>
    /// The backend stores compressed surfaces; it does not compress them.
    /// Encoding is the host's job (<see cref="Textures.ITextureEncoder"/>).
    /// </remarks>
    Task<WriteResult> ReplaceYtdTextureAsync(
        string sourcePath,
        string destinationPath,
        EncodedTexture texture,
        string? textureName = null,
        CancellationToken ct = default);

    /// <summary>
    /// Writes a copy of <paramref name="sourcePath"/> whose garment glows.
    /// </summary>
    /// <param name="multiplier">
    /// The shader's <c>emissiveMultiplier</c>. 1.0 is what Rockstar's own
    /// glowing garments use.
    /// </param>
    /// <remarks>
    /// <para>
    /// Measured in a running client: emission is the product of the vertex
    /// colour's blue channel, the diffuse alpha, and this multiplier. The
    /// backend supplies the first and the last; the alpha is the texture's, so
    /// what a design paints into alpha is what lights up.
    /// </para>
    /// <para>
    /// Swapping the shader alone does nothing, which is why this is one call
    /// rather than a material edit: base-ped meshes carry blue = 0 on every
    /// vertex, and the product collapses to zero however the material is
    /// declared. Splitting the two halves across the API would let a caller
    /// ship a garment that is emissive on paper and dark in game.
    /// </para>
    /// </remarks>
    Task<WriteResult> MakeEmissiveAsync(
        string sourcePath,
        string destinationPath,
        double multiplier = 1.0,
        CancellationToken ct = default);

    /// <summary>
    /// Derives a single-component addon <c>CPedVariationInfo</c> from a
    /// known-good ped YMT.
    /// </summary>
    Task<WriteResult> BuildAddonYmtAsync(
        string templatePath,
        string destinationPath,
        int component,
        int textureCount,
        uint dlcNameHash = 0,
        CancellationToken ct = default);

    /// <summary>
    /// Re-reads files we just wrote and confirms they still parse. Cheap, and
    /// it catches silent corruption before anything reaches a server.
    /// </summary>
    Task<PackValidationResult> ValidatePackAsync(
        IReadOnlyList<string> files,
        CancellationToken ct = default);
}

public sealed class RageAssetException : Exception
{
    public RageAssetException(string code, string message) : base(message) => Code = code;

    /// <summary>Stable code from the service contract, e.g. <c>parse_failed</c>.</summary>
    public string Code { get; }
}

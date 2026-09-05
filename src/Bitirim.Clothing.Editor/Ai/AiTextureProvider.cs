namespace Bitirim.Clothing.Editor.Ai;

/// <summary>What the model is being asked to do.</summary>
/// <remarks>
/// <c>Auto</c> is what the interface sends: edit when there is a texture to
/// edit, generate otherwise. Keeping the decision here rather than in the UI
/// means "make this black" cannot silently become "invent a new jacket"
/// because a panel forgot to attach the current diffuse.
/// </remarks>
public enum AiTextureMode
{
    Auto,
    Generate,
    Edit,
}

/// <summary>
/// One image sent alongside the prompt.
/// </summary>
/// <param name="Role">
/// Named in the prompt as well as attached, because a model given four
/// unlabelled images has to guess which is the mask and which is the garment.
/// </param>
public sealed record AiInputImage(AiImageRole Role, byte[] Data, string MimeType);

public enum AiImageRole
{
    /// <summary>The diffuse as it is now. Present for an edit, absent for a fresh generation.</summary>
    CurrentTexture,

    /// <summary>The UV islands drawn as wireframe, so seams and panels are visible.</summary>
    UvLayout,

    /// <summary>Filled islands: white where the garment uses the square, black elsewhere.</summary>
    UvMask,

    /// <summary>A render of the garment itself, for shape context.</summary>
    GarmentReference,
}

/// <summary>
/// What the texture belongs to. Goes into the prompt as plain description.
/// </summary>
public sealed record AiGarmentContext(
    string ComponentPrefix,
    string ComponentLabel,
    int DrawableIndex,
    string GarmentName,
    bool Male,
    int TextureSize);

/// <param name="Resolution">"1K", "2K" or "4K".</param>
/// <param name="AspectRatio">
/// Derived from the current texture, not assumed. A garment diffuse is square
/// far more often than it is 16:9, and a wrong ratio here is a stretched
/// garment in game.
/// </param>
public sealed record AiTextureRequest(
    string Prompt,
    AiTextureMode Mode,
    string Resolution,
    string AspectRatio,
    IReadOnlyList<AiInputImage> Images,
    AiGarmentContext Context)
{
    public AiInputImage? Image(AiImageRole role) => Images.FirstOrDefault(i => i.Role == role);

    /// <summary>
    /// The mode actually used, once <see cref="AiTextureMode.Auto"/> is resolved.
    /// </summary>
    public AiTextureMode EffectiveMode => Mode switch
    {
        AiTextureMode.Auto => Image(AiImageRole.CurrentTexture) is not null
            ? AiTextureMode.Edit
            : AiTextureMode.Generate,
        _ => Mode,
    };
}

/// <param name="Data">
/// The generated image, exactly as the model returned it. Nothing re-encodes
/// it on the way through: what the layer draws is what came back.
/// </param>
public sealed record AiTextureResult(
    byte[] Data,
    string MimeType,
    int Width,
    int Height,
    string Provider,
    string Model,
    AiTextureMode Mode,
    long ElapsedMs);

/// <param name="KeySource">
/// Where the key came from -- "environment", ".env" or "settings". Never the key.
/// </param>
public sealed record AiProviderStatus(
    string Id,
    string DisplayName,
    string Model,
    bool Configured,
    string? KeySource,
    string? Reason);

public sealed record AiConnectionCheck(bool Ok, string Message, string? Detail);

/// <summary>
/// Provider abstraction for AI texture generation.
/// </summary>
/// <remarks>
/// Nothing about a specific vendor leaks above this interface, and no
/// implementation ever returns an image it did not receive from a model. There
/// is no placeholder path: a provider that cannot generate throws, and the
/// interface reports the reason instead of offering a button that cannot work.
///
/// With no provider configured, every non-AI feature works unchanged -- AI is
/// additive, never load-bearing.
/// </remarks>
public interface IAiTextureProvider
{
    string Id { get; }

    /// <summary>
    /// Whether a key is present and what model would be used. Reads local
    /// configuration only -- it never calls the vendor, so opening the dialog
    /// costs nothing.
    /// </summary>
    AiProviderStatus Status { get; }

    Task<AiTextureResult> GenerateAsync(AiTextureRequest request, CancellationToken ct = default);

    /// <summary>
    /// Verifies the key and the model, on demand. Separate from
    /// <see cref="Status"/> because this one does reach the network.
    /// </summary>
    Task<AiConnectionCheck> TestConnectionAsync(CancellationToken ct = default);
}

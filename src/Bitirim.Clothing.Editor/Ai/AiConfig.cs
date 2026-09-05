namespace Bitirim.Clothing.Editor.Ai;

/// <summary>
/// Static configuration for AI texture generation.
/// </summary>
/// <remarks>
/// The model name lives here rather than in the provider so that changing it is
/// a one-line edit in a file whose only job is configuration. Settings can
/// override the model per install; nothing else about the request is tunable
/// from the interface, because the rest of it is what makes the output usable
/// as a garment diffuse rather than a picture of a garment.
/// </remarks>
public static class AiConfig
{
    /// <summary>The provider used when nothing else is chosen.</summary>
    public const string DefaultImageProvider = "gemini";

    /// <summary>
    /// The image model. Gemini 3.1 Flash Image: multimodal in, image out.
    /// </summary>
    public const string Model = "gemini-3.1-flash-image";

    /// <summary>Output size when the caller does not ask for one.</summary>
    public const string DefaultResolution = "2K";

    /// <summary>Sizes the model accepts, in the spelling it expects.</summary>
    public static readonly IReadOnlyList<string> Resolutions = new[] { "1K", "2K", "4K" };

    /// <summary>Aspect ratio used when the current texture's own shape is unknown.</summary>
    public const string DefaultAspectRatio = "1:1";

    /// <summary>
    /// How long one generation may take before we stop waiting.
    /// </summary>
    /// <remarks>
    /// A 4K image round-trip is not fast. This is deliberately generous; the
    /// interface stays responsive because the call is off the UI thread and the
    /// dialog can be closed while it runs.
    /// </remarks>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(180);

    /// <summary>
    /// Largest input image we will upload, per image.
    /// </summary>
    /// <remarks>
    /// A 4K PNG of a UV mask is a few hundred KB; a screenshot pasted in by
    /// hand could be far larger. Refusing early gives a sentence the user can
    /// act on instead of a 400 from the API.
    /// </remarks>
    public const int MaxInputImageBytes = 8 * 1024 * 1024;

    /// <summary>Prefix on every AI log line, so the channel is greppable.</summary>
    public const string LogPrefix = "[AI_TEXTURE]";

    public static bool IsKnownResolution(string? value) =>
        value is not null && Resolutions.Contains(value, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Normalises a resolution, falling back to the default.
    /// </summary>
    public static string NormaliseResolution(string? value) =>
        IsKnownResolution(value)
            ? Resolutions.First(r => string.Equals(r, value, StringComparison.OrdinalIgnoreCase))
            : DefaultResolution;
}

using System.Diagnostics;
using Bitirim.Clothing.Editor.Infrastructure;
using Bitirim.Clothing.Editor.Settings;
using Google.GenAI;
using Google.GenAI.Types;

namespace Bitirim.Clothing.Editor.Ai;

/// <summary>
/// Texture generation through Gemini, using Google's own .NET SDK.
/// </summary>
/// <remarks>
/// The model is multimodal in and image out: the prompt, the current diffuse,
/// the UV layout and the UV mask all go in one turn, and one image comes back.
/// Nothing downstream of here changes -- the returned image enters the editor as
/// an ordinary image layer and goes through the same crop, placement and export
/// path as an imported file. Gemini does the drawing; UV placement, compositing
/// and validation stay this application's job.
///
/// There is no offline or demo path. If the model returns no image, this throws
/// with the reason, and the interface says so rather than showing a placeholder.
/// </remarks>
public sealed class GeminiTextureProvider : IAiTextureProvider
{
    private static readonly string[] AcceptedInputMimeTypes =
        { "image/png", "image/jpeg", "image/webp" };

    private readonly SettingsService _settings;
    private readonly LogService _log;

    public GeminiTextureProvider(SettingsService settings, LogService log)
    {
        _settings = settings;
        _log = log;
    }

    public string Id => "gemini";

    /// <summary>
    /// The model actually used: whatever Settings names, or the configured
    /// default. Settings is an override, not a requirement.
    /// </summary>
    public string Model =>
        string.IsNullOrWhiteSpace(_settings.Current.AiModel)
            ? AiConfig.Model
            : _settings.Current.AiModel!.Trim();

    public AiProviderStatus Status
    {
        get
        {
            var key = AiKeyResolver.Resolve(_settings);
            return new AiProviderStatus(
                Id: Id,
                DisplayName: "Google Gemini",
                Model: Model,
                Configured: key is not null,
                KeySource: key?.Source,
                Reason: key is null
                    ? "No Gemini API key is configured. Set GEMINI_API_KEY in a .env file, "
                      + "or enter a key under Settings > AI."
                    : null);
        }
    }

    // ------------------------------------------------------------------
    // connection check
    // ------------------------------------------------------------------

    /// <summary>
    /// Asks the API to describe the model.
    /// </summary>
    /// <remarks>
    /// Deliberately not a generation: this proves the key works and the model
    /// exists without spending an image. It only runs when the user presses the
    /// button, so opening the dialog costs nothing.
    /// </remarks>
    public async Task<AiConnectionCheck> TestConnectionAsync(CancellationToken ct = default)
    {
        var key = AiKeyResolver.Resolve(_settings);
        if (key is null)
        {
            return new AiConnectionCheck(false, "No API key is configured.",
                "Set GEMINI_API_KEY in a .env file, or enter a key under Settings > AI.");
        }

        try
        {
            var client = CreateClient(key.Value);
            using var timeout = Linked(ct);
            var model = await client.Models
                .GetAsync(Model, cancellationToken: timeout.Token)
                .ConfigureAwait(false);

            _log.Info($"{AiConfig.LogPrefix} connection ok: model={Model} key_source={key.Source}");
            return new AiConnectionCheck(true, $"Connected. Model \"{Model}\" is available.",
                model.DisplayName);
        }
        catch (Exception ex)
        {
            var failure = Describe(ex);
            _log.Warn($"{AiConfig.LogPrefix} connection failed: {failure.Code}");
            _log.Error($"{AiConfig.LogPrefix} connection failure detail", ex);
            return new AiConnectionCheck(false, failure.Message, failure.Hint);
        }
    }

    // ------------------------------------------------------------------
    // generation
    // ------------------------------------------------------------------

    public async Task<AiTextureResult> GenerateAsync(
        AiTextureRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            throw new EditorException("ai_empty_prompt",
                "Describe what you want before generating.",
                "The model has nothing to work from without a prompt.");
        }

        var key = AiKeyResolver.Resolve(_settings)
                  ?? throw new EditorException("ai_no_key",
                      "No Gemini API key is configured.",
                      "Set GEMINI_API_KEY in a .env file next to the application, "
                      + "or enter a key under Settings > AI.");

        var ordered = Validate(request.Images);
        var mode = request.EffectiveMode;
        var resolution = AiConfig.NormaliseResolution(request.Resolution);

        var parts = new List<Part> { Part.FromText(GeminiPrompt.BuildUserPrompt(request, ordered)) };
        foreach (var image in ordered)
            parts.Add(Part.FromBytes(image.Data, image.MimeType));

        var contents = new List<Content> { new() { Role = "user", Parts = parts } };

        _log.Info($"{AiConfig.LogPrefix} generate: model={Model} "
                  + $"mode={mode.ToString().ToLowerInvariant()} resolution={resolution} "
                  + $"aspect={request.AspectRatio} images={ordered.Count} "
                  + $"prompt_chars={request.Prompt.Trim().Length} key_source={key.Source}");

        var client = CreateClient(key.Value);
        var stopwatch = Stopwatch.StartNew();

        GenerateContentResponse response;
        try
        {
            using var timeout = Linked(ct);
            response = await SendAsync(client, contents, request, resolution, timeout.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _log.Warn($"{AiConfig.LogPrefix} timed out after {AiConfig.Timeout.TotalSeconds:0}s");
            throw new EditorException("ai_timeout",
                $"The model did not answer within {AiConfig.Timeout.TotalSeconds:0} seconds.",
                "A 4K image takes longer than a 1K one. Try again, or drop the resolution.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var failure = Describe(ex);
            _log.Warn($"{AiConfig.LogPrefix} generation failed: {failure.Code}");
            _log.Error($"{AiConfig.LogPrefix} generation failure detail", ex);
            throw new EditorException(failure.Code, failure.Message, failure.Hint);
        }

        stopwatch.Stop();

        var returned = FirstImage(response) ?? throw NoImage(response);

        var extension = ImageProbe.ExtensionFor(returned.MimeType);
        if (extension is null)
        {
            _log.Warn($"{AiConfig.LogPrefix} unsupported output mime: {returned.MimeType}");
            throw new EditorException("ai_unsupported_output",
                $"The model returned a \"{returned.MimeType}\" image, which cannot be used as a texture.",
                "PNG, JPEG and WEBP can all be used as a layer image; anything else cannot. "
                + "See logs/errors.log.");
        }

        var data = returned.Data!;
        var (width, height) = ImageProbe.Measure(data);
        _log.Info($"{AiConfig.LogPrefix} generated: {width}x{height} {returned.MimeType} "
                  + $"{data.Length / 1024} KB in {stopwatch.ElapsedMilliseconds} ms");

        return new AiTextureResult(
            Data: data,
            MimeType: returned.MimeType!,
            Width: width,
            Height: height,
            Provider: Id,
            Model: Model,
            Mode: mode,
            ElapsedMs: stopwatch.ElapsedMilliseconds);
    }

    /// <summary>
    /// Sends the turn, asking for an image back.
    /// </summary>
    /// <remarks>
    /// Image-only is the documented response modality for the image models, but
    /// which forms are accepted has moved between model generations. A rejection
    /// that names the modality is retried once with text and image together,
    /// which every generation accepts; any text that comes back is ignored.
    /// Every other error is passed straight up -- this is not a blanket retry.
    /// </remarks>
    private async Task<GenerateContentResponse> SendAsync(
        Client client,
        List<Content> contents,
        AiTextureRequest request,
        string resolution,
        CancellationToken ct)
    {
        try
        {
            return await client.Models
                .GenerateContentAsync(Model, contents, Config(request, resolution, imageOnly: true), ct)
                .ConfigureAwait(false);
        }
        catch (ClientError ex) when (ex.StatusCode == 400 && MentionsModality(ex.Message))
        {
            _log.Info($"{AiConfig.LogPrefix} image-only response modality refused; "
                      + "retrying with text and image");
            return await client.Models
                .GenerateContentAsync(Model, contents, Config(request, resolution, imageOnly: false), ct)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Whether a 429 means "you have none of this" rather than "not so fast".
    /// </summary>
    /// <remarks>
    /// The API says both with the same status code. The free tier reports
    /// <c>limit: 0</c> for the image models, which is a different problem with
    /// a different fix, and the message has to say so.
    /// </remarks>
    internal static bool QuotaIsZero(string? message) =>
        message is not null && message.Contains("limit: 0", StringComparison.OrdinalIgnoreCase);

    private static bool MentionsModality(string? message) =>
        message is not null && message.Contains("modalit", StringComparison.OrdinalIgnoreCase);

    private static GenerateContentConfig Config(
        AiTextureRequest request, string resolution, bool imageOnly) => new()
    {
        ResponseModalities = imageOnly
            ? new List<string> { "IMAGE" }
            : new List<string> { "TEXT", "IMAGE" },

        SystemInstruction = new Content
        {
            Parts = new List<Part> { Part.FromText(GeminiPrompt.GEMINI_TEXTURE_SYSTEM_PROMPT) },
        },

        // Only the two settings the image models actually need. ImageConfig
        // also carries OutputMimeType and PersonGeneration, and both were
        // tempting -- but a value one of those refuses is a 400 that kills
        // every generation, and neither buys anything here: the system prompt
        // already forbids people, and the response handler accepts PNG, JPEG
        // or WEBP and stores whichever arrives.
        ImageConfig = new ImageConfig
        {
            // Taken from the texture being edited, never assumed. A garment
            // diffuse is square; a hardcoded 16:9 would stretch it in game.
            AspectRatio = string.IsNullOrWhiteSpace(request.AspectRatio)
                ? AiConfig.DefaultAspectRatio
                : request.AspectRatio,
            ImageSize = resolution,
        },
    };

    private static Client CreateClient(string apiKey) => new(apiKey: apiKey, vertexAI: false);

    private static CancellationTokenSource Linked(CancellationToken ct)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(ct);
        source.CancelAfter(AiConfig.Timeout);
        return source;
    }

    // ------------------------------------------------------------------
    // response handling
    // ------------------------------------------------------------------

    private static Blob? FirstImage(GenerateContentResponse response)
    {
        foreach (var part in EnumerateParts(response))
        {
            var blob = part.InlineData;
            if (blob?.Data is { Length: > 0 }
                && blob.MimeType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true)
            {
                return blob;
            }
        }

        return null;
    }

    private static IEnumerable<Part> EnumerateParts(GenerateContentResponse response)
    {
        foreach (var candidate in response.Candidates ?? new List<Candidate>())
        {
            foreach (var part in candidate.Content?.Parts ?? new List<Part>())
                yield return part;
        }

        // Responses also surface the parts directly. Both are read, in case
        // only one of them is populated.
        foreach (var part in response.Parts ?? new List<Part>())
            yield return part;
    }

    /// <summary>
    /// Turns "no image came back" into the actual reason, when there is one.
    /// </summary>
    private static EditorException NoImage(GenerateContentResponse response)
    {
        var blocked = response.PromptFeedback?.BlockReason;
        if (blocked is not null)
        {
            return new EditorException("ai_blocked",
                "The request was blocked by the model's safety filters.",
                response.PromptFeedback?.BlockReasonMessage ?? "Rephrase the prompt and try again.");
        }

        var candidate = response.Candidates?.FirstOrDefault();
        if (candidate?.FinishReason is { } finish && finish != FinishReason.Stop)
        {
            // The image models have their own finish reasons, and they say
            // something quite different from "it stopped early".
            if (finish == FinishReason.ImageSafety
                || finish == FinishReason.ImageProhibitedContent
                || finish == FinishReason.Safety
                || finish == FinishReason.ProhibitedContent)
            {
                return new EditorException("ai_blocked",
                    "The model's safety filters refused to produce this image.",
                    candidate.FinishMessage
                    ?? "Rephrase the prompt. Garment descriptions that read as a person "
                       + "wearing the clothing are the usual cause.");
            }

            if (finish == FinishReason.ImageRecitation || finish == FinishReason.Recitation)
            {
                return new EditorException("ai_blocked",
                    "The model refused because the result looked like a copy of existing work.",
                    "Describe the material and colours rather than naming a brand or a "
                    + "specific real garment.");
            }

            return new EditorException("ai_no_image",
                $"The model stopped without producing an image ({finish.Value}).",
                candidate.FinishMessage ?? "Rephrase the prompt and try again.");
        }

        return new EditorException("ai_no_image",
            "The model answered without an image.",
            "This usually means the prompt was read as a question. "
            + "Describe the texture you want rather than asking about it.");
    }

    // ------------------------------------------------------------------
    // input validation
    // ------------------------------------------------------------------

    /// <summary>
    /// Checks the attachments and puts them in the order the prompt names them.
    /// </summary>
    /// <remarks>
    /// The order matters: the prompt refers to "IMAGE 1", "IMAGE 2" and so on,
    /// and the roles have to line up with the attachments, or the model is
    /// being told that the mask is the texture.
    /// </remarks>
    internal static List<AiInputImage> Validate(IReadOnlyList<AiInputImage> images)
    {
        var order = new[]
        {
            AiImageRole.CurrentTexture,
            AiImageRole.UvLayout,
            AiImageRole.UvMask,
            AiImageRole.GarmentReference,
        };

        var ordered = new List<AiInputImage>();
        foreach (var role in order)
        {
            var image = images.FirstOrDefault(i => i.Role == role);
            if (image is null) continue;

            if (image.Data.Length == 0)
            {
                throw new EditorException("ai_bad_image",
                    $"The {Label(role)} image is empty.",
                    "This is a fault in the application rather than in the prompt.");
            }

            if (image.Data.Length > AiConfig.MaxInputImageBytes)
            {
                throw new EditorException("ai_image_too_large",
                    $"The {Label(role)} image is {image.Data.Length / 1024 / 1024} MB, over the "
                    + $"{AiConfig.MaxInputImageBytes / 1024 / 1024} MB limit.",
                    "Lower the project's authoring resolution and try again.");
            }

            if (!AcceptedInputMimeTypes.Contains(image.MimeType, StringComparer.OrdinalIgnoreCase))
            {
                throw new EditorException("ai_unsupported_input",
                    $"A \"{image.MimeType}\" image cannot be sent to the model.",
                    "PNG, JPEG and WEBP are accepted.");
            }

            ordered.Add(image);
        }

        return ordered;
    }

    private static string Label(AiImageRole role) => role switch
    {
        AiImageRole.CurrentTexture => "current texture",
        AiImageRole.UvLayout => "UV layout",
        AiImageRole.UvMask => "UV mask",
        AiImageRole.GarmentReference => "garment reference",
        _ => "reference",
    };

    // ------------------------------------------------------------------
    // errors
    // ------------------------------------------------------------------

    /// <summary>
    /// Maps a failure to something a person can act on.
    /// </summary>
    /// <remarks>
    /// The vendor's own wording is kept out of the interface: it names
    /// endpoints, quotas and sometimes the request itself, and none of that
    /// helps someone who wanted a black jacket. The full exception goes to
    /// errors.log with a reference the user can quote.
    /// </remarks>
    internal static (string Code, string Message, string Hint) Describe(Exception ex) => ex switch
    {
        ClientError { StatusCode: 400 } => (
            "ai_bad_request",
            "The model rejected the request.",
            "The prompt or one of the attached images was not accepted. "
            + "See logs/errors.log for the API's own wording."),

        ClientError { StatusCode: 401 or 403 } => (
            "ai_bad_key",
            "The Gemini API key was rejected.",
            "Check the key under Settings > AI, or the GEMINI_API_KEY value in your .env file."),

        ClientError { StatusCode: 404 } => (
            "ai_model_missing",
            "The configured model is not available to this API key.",
            $"The application asks for \"{AiConfig.Model}\". "
            + "The key may not have access to it yet."),

        // A free-tier key is not merely throttled on the image models: the
        // quota is zero, so no amount of waiting helps. Telling someone to try
        // again in a minute when the answer is "this needs billing enabled"
        // wastes an evening, so the two are separated by what the quota says.
        ClientError { StatusCode: 429 } e when QuotaIsZero(e.Message) => (
            "ai_quota_zero",
            "This API key has no quota for the image model at all.",
            "The Gemini free tier grants zero image generations. Enable billing on the "
            + "Google Cloud project behind the key (aistudio.google.com/apikey shows which "
            + "project it belongs to), then try again. Waiting will not help."),

        ClientError { StatusCode: 429 } => (
            "ai_rate_limited",
            "The API is rate limiting.",
            "Too many requests too quickly. Wait a moment and try again."),

        ClientError { StatusCode: >= 500 } or ServerError => (
            "ai_server_error",
            "The model service reported an error on its side.",
            "Nothing is wrong with the request. Try again shortly."),

        ClientError => (
            "ai_client_error",
            "The model service refused the request.",
            "See logs/errors.log for the API's own wording."),

        HttpRequestException => (
            "ai_network",
            "The model service could not be reached.",
            "Check the network connection, then try again."),

        TaskCanceledException or TimeoutException => (
            "ai_timeout",
            "The request timed out.",
            "Try again, or drop the resolution."),

        _ => (
            "ai_failed",
            "The texture could not be generated.",
            "See logs/errors.log for detail."),
    };
}

using System.Text.Json;
using Bitirim.Clothing.Core.Naming;
using Bitirim.Clothing.Editor.Ai;
using Bitirim.Clothing.Editor.Infrastructure;
using Bitirim.Clothing.Editor.Projects;

namespace Bitirim.Clothing.Desktop.Host;

/// <summary>
/// The AI texture generation operations.
/// </summary>
/// <remarks>
/// Everything that touches the API key happens on this side of the bridge. The
/// interface sends a prompt and some images and receives a file path back; it
/// never sees the key, the model endpoint or the vendor's own error text.
///
/// The generated image lands in the project's <c>layers/</c> folder like any
/// imported file, which is what puts it into the existing pipeline: preview,
/// crop and fit, UV-aware placement, movable image layer. This code adds a
/// source of images. It does not add a second way to make a texture.
/// </remarks>
public static class AiOperations
{
    public static void Register(HostBridge bridge, AppSession session)
    {
        // Local configuration only. Opening the dialog must not cost a request,
        // so nothing here reaches the network.
        bridge.On("ai.status", _ =>
        {
            var status = session.Ai.Image.Status;
            return new
            {
                providerId = status.Id,
                displayName = status.DisplayName,
                model = status.Model,
                configured = status.Configured,
                // "environment", ".env" or "settings" -- where, never what.
                keySource = status.KeySource,
                reason = status.Reason,
                resolutions = AiConfig.Resolutions,
                defaultResolution = AiConfig.DefaultResolution,
                busy = session.RunningAi is not null,
            };
        });

        // Explicit, user-pressed. Asks the API to describe the model, which
        // proves the key and the model without spending an image.
        bridge.On("ai.testConnection", async _ =>
        {
            var check = await session.Ai.Image.TestConnectionAsync().ConfigureAwait(false);
            return new { ok = check.Ok, message = check.Message, detail = check.Detail };
        });

        bridge.On("ai.cancel", _ =>
        {
            var running = session.RunningAi;
            if (running is null) return new { cancelled = false };

            running.Cancel();
            session.Log.Info($"{AiConfig.LogPrefix} cancelled by the user");
            return new { cancelled = true };
        });

        bridge.On("ai.generateTexture", args => GenerateAsync(session, args));
    }

    private static async Task<object?> GenerateAsync(AppSession session, JsonElement args)
    {
        var project = session.RequireProject();

        // One request at a time. The interface also disables its own button,
        // but a double-click that races the state update must not turn into two
        // billed calls.
        if (session.RunningAi is not null)
        {
            throw new EditorException("ai_busy",
                "A texture is already being generated.",
                "Wait for it to finish, or cancel it.");
        }

        var prompt = HostBridge.OptionalString(args, "prompt") ?? "";
        if (string.IsNullOrWhiteSpace(prompt))
        {
            throw new EditorException("ai_empty_prompt",
                "Describe what you want before generating.",
                "The model has nothing to work from without a prompt.");
        }

        var request = new AiTextureRequest(
            Prompt: prompt,
            Mode: ParseMode(HostBridge.OptionalString(args, "mode")),
            Resolution: AiConfig.NormaliseResolution(HostBridge.OptionalString(args, "resolution")),
            AspectRatio: HostBridge.OptionalString(args, "aspectRatio") ?? AiConfig.DefaultAspectRatio,
            Images: ReadImages(args),
            Context: ContextFor(project));

        var source = new CancellationTokenSource();
        session.RunningAi = source;

        AiTextureResult result;
        try
        {
            result = await session.Ai.Image.GenerateAsync(request, source.Token)
                .ConfigureAwait(false);
        }
        finally
        {
            session.RunningAi = null;
            source.Dispose();
        }

        var relative = Write(project, result);
        session.Dirty = true;

        return new
        {
            relativePath = relative,
            dataUrl = $"data:{result.MimeType};base64,{Convert.ToBase64String(result.Data)}",
            width = result.Width,
            height = result.Height,
            mimeType = result.MimeType,
            provider = result.Provider,
            model = result.Model,
            mode = result.Mode.ToString().ToLowerInvariant(),
            elapsedMs = result.ElapsedMs,
            bytes = result.Data.Length,
        };
    }

    /// <summary>
    /// Stores the generated image in the project, next to imported layer images.
    /// </summary>
    /// <remarks>
    /// Written byte for byte as the model returned it. Re-encoding here would
    /// mean the texture that ends up in game is not the one that was reviewed
    /// in the preview.
    /// </remarks>
    private static string Write(OpenProject project, AiTextureResult result)
    {
        var directory = Path.Combine(project.Directory, "layers");
        Directory.CreateDirectory(directory);

        var extension = ImageProbe.ExtensionFor(result.MimeType) ?? ".png";
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var destination = Path.Combine(directory, $"ai-{stamp}{extension}");

        var n = 2;
        while (File.Exists(destination))
            destination = Path.Combine(directory, $"ai-{stamp} ({n++}){extension}");

        File.WriteAllBytes(destination, result.Data);
        return Path.GetRelativePath(project.Directory, destination).Replace('\\', '/');
    }

    private static AiGarmentContext ContextFor(OpenProject project)
    {
        var document = project.Document;
        var asset = document.Active;

        return new AiGarmentContext(
            ComponentPrefix: ClothingNames.Prefix(asset.Component),
            ComponentLabel: ClothingNames.Label(asset.Component),
            DrawableIndex: asset.DrawableIndex,
            GarmentName: asset.Name,
            Male: document.Male,
            TextureSize: document.Settings.TextureSize);
    }

    private static AiTextureMode ParseMode(string? value) => value?.ToLowerInvariant() switch
    {
        "generate" => AiTextureMode.Generate,
        "edit" => AiTextureMode.Edit,
        _ => AiTextureMode.Auto,
    };

    /// <summary>
    /// Reads the attachments the interface sent, each labelled with its role.
    /// </summary>
    /// <remarks>
    /// An unrecognised role is dropped rather than guessed at: sending the mask
    /// where the texture should be is worse than sending one image fewer.
    /// </remarks>
    private static List<AiInputImage> ReadImages(JsonElement args)
    {
        var images = new List<AiInputImage>();
        if (args.ValueKind != JsonValueKind.Object
            || !args.TryGetProperty("images", out var list)
            || list.ValueKind != JsonValueKind.Array)
        {
            return images;
        }

        foreach (var entry in list.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object) continue;

            var role = ParseRole(HostBridge.OptionalString(entry, "role"));
            var dataUrl = HostBridge.OptionalString(entry, "dataUrl");
            if (role is null || string.IsNullOrWhiteSpace(dataUrl)) continue;

            var decoded = DecodeDataUrl(dataUrl);
            if (decoded is null) continue;

            images.Add(new AiInputImage(role.Value, decoded.Value.Data, decoded.Value.MimeType));
        }

        return images;
    }

    private static AiImageRole? ParseRole(string? value) => value?.ToLowerInvariant() switch
    {
        "currenttexture" => AiImageRole.CurrentTexture,
        "uvlayout" => AiImageRole.UvLayout,
        "uvmask" => AiImageRole.UvMask,
        "garmentreference" => AiImageRole.GarmentReference,
        _ => null,
    };

    private static (byte[] Data, string MimeType)? DecodeDataUrl(string dataUrl)
    {
        if (!dataUrl.StartsWith("data:", StringComparison.Ordinal)) return null;

        var comma = dataUrl.IndexOf(',');
        if (comma < 0) return null;

        var header = dataUrl[5..comma];
        if (!header.Contains("base64", StringComparison.OrdinalIgnoreCase)) return null;

        var semicolon = header.IndexOf(';');
        var mime = (semicolon < 0 ? header : header[..semicolon]).Trim();
        if (mime.Length == 0) mime = "image/png";

        try
        {
            return (Convert.FromBase64String(dataUrl[(comma + 1)..]), mime);
        }
        catch (FormatException)
        {
            throw new EditorException("ai_bad_image",
                "One of the reference images could not be read.");
        }
    }
}

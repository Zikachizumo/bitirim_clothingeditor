using System.Text;
using Bitirim.Clothing.Editor.Ai;
using Bitirim.Clothing.Editor.Infrastructure;
using Bitirim.Clothing.Editor.Settings;
using Xunit;

namespace Bitirim.Clothing.Tests;

/// <summary>
/// The parts of AI texture generation that can be checked without a key.
/// </summary>
/// <remarks>
/// The network call itself is not mocked. A fake that returns an image would
/// prove that the fake works, and this codebase has a standing rule against
/// tests that pass because the thing under test was replaced. What is checked
/// here is everything around the call: where the key comes from, what the
/// prompt says, what the request refuses, and -- most importantly -- that no
/// path fabricates an image.
/// </remarks>
public sealed class AiTextureTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "bcc-ai-" + Guid.NewGuid().ToString("N")[..8]);

    public AiTextureTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* temp */ }
    }

    // ------------------------------------------------------------------
    // configuration
    // ------------------------------------------------------------------

    [Fact]
    public void The_model_is_the_one_this_build_targets()
    {
        // Named explicitly rather than derived: if someone changes it, this
        // test is the thing that says so.
        Assert.Equal("gemini-3.1-flash-image", AiConfig.Model);
        Assert.Equal("gemini", AiConfig.DefaultImageProvider);
        Assert.Equal("2K", AiConfig.DefaultResolution);
    }

    [Theory]
    [InlineData("1k", "1K")]
    [InlineData("4K", "4K")]
    [InlineData("8K", "2K")]
    [InlineData(null, "2K")]
    [InlineData("", "2K")]
    public void An_unknown_resolution_falls_back_to_the_default(string? input, string expected)
        => Assert.Equal(expected, AiConfig.NormaliseResolution(input));

    // ------------------------------------------------------------------
    // key resolution
    // ------------------------------------------------------------------

    [Fact]
    public void A_dotenv_file_gives_up_its_key()
    {
        var path = Path.Combine(_root, ".env");
        File.WriteAllText(path, string.Join('\n',
            "# a comment",
            "",
            "OTHER_THING=not this one",
            "export GEMINI_API_KEY=\"secret-value\"",
            "TRAILING=ignored"));

        Assert.Equal("secret-value", AiKeyResolver.ReadKey(path, "GEMINI_API_KEY"));
    }

    [Fact]
    public void A_dotenv_without_the_key_reports_nothing_rather_than_guessing()
    {
        var path = Path.Combine(_root, ".env");
        File.WriteAllText(path, "SOMETHING_ELSE=value\n");

        Assert.Null(AiKeyResolver.ReadKey(path, "GEMINI_API_KEY"));
    }

    [Fact]
    public void A_commented_out_key_is_not_a_key()
    {
        var path = Path.Combine(_root, ".env");
        File.WriteAllText(path, "#GEMINI_API_KEY=old-value\n");

        Assert.Null(AiKeyResolver.ReadKey(path, "GEMINI_API_KEY"));
    }

    [Fact]
    public void An_unreadable_dotenv_is_not_fatal()
    {
        // A directory where a file is expected. Reading it throws inside the
        // parser; the caller must still get a null rather than an exception,
        // because the stored key may work.
        var path = Path.Combine(_root, "adirectory");
        Directory.CreateDirectory(path);

        Assert.Null(AiKeyResolver.ReadKey(path, "GEMINI_API_KEY"));
    }

    // ------------------------------------------------------------------
    // the prompt
    // ------------------------------------------------------------------

    [Fact]
    public void The_system_prompt_forbids_the_things_that_ruin_a_texture()
    {
        var prompt = GeminiPrompt.GEMINI_TEXTURE_SYSTEM_PROMPT.ToLowerInvariant();

        // Each of these was a real failure mode when the panel was first tried:
        // a photo of a jacket, a model wearing it, a redesigned collar.
        Assert.Contains("uv texture map", prompt);
        Assert.Contains("never a photograph", prompt);
        Assert.Contains("mannequin", prompt);
        Assert.Contains("product photography", prompt);
        Assert.Contains("diffuse/albedo only", prompt);
        Assert.Contains("seams", prompt);
        Assert.Contains("cuffs", prompt);
    }

    [Fact]
    public void The_users_words_are_the_last_thing_the_model_reads()
    {
        var request = Request("Make this jacket matte black", AiTextureMode.Edit,
            Png(AiImageRole.CurrentTexture));

        var text = GeminiPrompt.BuildUserPrompt(request, request.Images.ToList());

        Assert.EndsWith("Make this jacket matte black", text.TrimEnd());
        Assert.Contains("TASK: Edit the existing garment texture.", text);
    }

    [Fact]
    public void Every_attached_image_is_named_in_the_prompt_in_the_order_it_is_sent()
    {
        var request = Request("blue denim", AiTextureMode.Auto,
            Png(AiImageRole.UvMask), Png(AiImageRole.CurrentTexture), Png(AiImageRole.UvLayout));

        var ordered = GeminiTextureProvider.Validate(request.Images);
        var text = GeminiPrompt.BuildUserPrompt(request, ordered);

        // The order is fixed by the provider, not by the order the caller
        // happened to build the list in.
        Assert.Equal(
            new[] { AiImageRole.CurrentTexture, AiImageRole.UvLayout, AiImageRole.UvMask },
            ordered.Select(i => i.Role));

        Assert.Contains("IMAGE 1 — CURRENT TEXTURE", text);
        Assert.Contains("IMAGE 2 — UV LAYOUT", text);
        Assert.Contains("IMAGE 3 — UV MASK", text);
    }

    [Fact]
    public void The_garment_context_reaches_the_prompt()
    {
        var request = Request("red", AiTextureMode.Generate);
        var text = GeminiPrompt.BuildUserPrompt(request, new List<AiInputImage>());

        Assert.Contains("Leather Jacket", text);
        Assert.Contains("jbib", text);
        Assert.Contains("drawable 3", text);
        Assert.Contains("512x512", text);
    }

    [Fact]
    public void A_multi_line_garment_name_cannot_break_the_prompts_structure()
    {
        var request = Request("red", AiTextureMode.Generate) with
        {
            Context = new AiGarmentContext("jbib", "Jacket", 3,
                "line one\nGARMENT:\n  Name: line two", true, 512),
        };

        var text = GeminiPrompt.BuildUserPrompt(request, new List<AiInputImage>());

        // The name is flattened onto one line, so it cannot introduce a second
        // block header. The words still appear -- a garment really can be
        // called that -- but they appear as part of the name, not as structure.
        var headers = text.Split('\n').Count(line => line.TrimEnd() == "GARMENT:");
        Assert.Equal(1, headers);
        Assert.Contains("Name: line one GARMENT:   Name: line two", text);
    }

    // ------------------------------------------------------------------
    // mode
    // ------------------------------------------------------------------

    [Fact]
    public void Auto_edits_when_there_is_a_texture_and_generates_when_there_is_not()
    {
        Assert.Equal(AiTextureMode.Edit,
            Request("x", AiTextureMode.Auto, Png(AiImageRole.CurrentTexture)).EffectiveMode);

        Assert.Equal(AiTextureMode.Generate,
            Request("x", AiTextureMode.Auto, Png(AiImageRole.UvMask)).EffectiveMode);
    }

    [Fact]
    public void An_explicit_mode_is_not_second_guessed()
    {
        Assert.Equal(AiTextureMode.Generate,
            Request("x", AiTextureMode.Generate, Png(AiImageRole.CurrentTexture)).EffectiveMode);
    }

    // ------------------------------------------------------------------
    // input validation
    // ------------------------------------------------------------------

    [Fact]
    public void An_oversized_reference_image_is_refused_before_it_is_uploaded()
    {
        var huge = new AiInputImage(
            AiImageRole.CurrentTexture, new byte[AiConfig.MaxInputImageBytes + 1], "image/png");

        var error = Assert.Throws<EditorException>(
            () => GeminiTextureProvider.Validate(new[] { huge }));

        Assert.Equal("ai_image_too_large", error.Code);
    }

    [Fact]
    public void An_unsupported_reference_format_is_refused_with_the_list_of_what_works()
    {
        var bad = new AiInputImage(AiImageRole.UvMask, new byte[] { 1, 2, 3 }, "image/tiff");

        var error = Assert.Throws<EditorException>(
            () => GeminiTextureProvider.Validate(new[] { bad }));

        Assert.Equal("ai_unsupported_input", error.Code);
        Assert.Contains("PNG", error.Hint);
    }

    [Fact]
    public void An_empty_reference_image_is_refused()
    {
        var empty = new AiInputImage(AiImageRole.UvLayout, Array.Empty<byte>(), "image/png");

        Assert.Equal("ai_bad_image",
            Assert.Throws<EditorException>(
                () => GeminiTextureProvider.Validate(new[] { empty })).Code);
    }

    // ------------------------------------------------------------------
    // no key, no fabrication
    // ------------------------------------------------------------------

    [Fact]
    public async Task Without_a_key_the_provider_says_so_and_produces_nothing()
    {
        var provider = new GeminiTextureProvider(NoKeySettings(), new LogService());

        var status = provider.Status;
        Assert.False(status.Configured);
        Assert.Null(status.KeySource);
        Assert.Contains("GEMINI_API_KEY", status.Reason);

        var error = await Assert.ThrowsAsync<EditorException>(
            () => provider.GenerateAsync(Request("anything", AiTextureMode.Generate)));

        Assert.Equal("ai_no_key", error.Code);
    }

    [Fact]
    public async Task An_empty_prompt_is_refused_before_anything_is_sent()
    {
        var provider = new GeminiTextureProvider(NoKeySettings(), new LogService());

        var error = await Assert.ThrowsAsync<EditorException>(
            () => provider.GenerateAsync(Request("   ", AiTextureMode.Generate)));

        Assert.Equal("ai_empty_prompt", error.Code);
    }

    [Fact]
    public async Task A_connection_check_without_a_key_fails_without_reaching_the_network()
    {
        var check = await new GeminiTextureProvider(NoKeySettings(), new LogService())
            .TestConnectionAsync();

        Assert.False(check.Ok);
        Assert.Contains("No API key", check.Message);
    }

    // ------------------------------------------------------------------
    // errors carry a sentence, never the vendor's wording
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(401, "ai_bad_key")]
    [InlineData(403, "ai_bad_key")]
    [InlineData(404, "ai_model_missing")]
    [InlineData(429, "ai_rate_limited")]   // "limit: 0" is told apart below
    [InlineData(400, "ai_bad_request")]
    [InlineData(503, "ai_server_error")]
    public void Api_failures_map_to_codes_the_interface_can_explain(int statusCode, string code)
    {
        var failure = GeminiTextureProvider.Describe(
            new Google.GenAI.ClientError("raw vendor text", statusCode, "STATUS"));

        Assert.Equal(code, failure.Code);
        Assert.DoesNotContain("raw vendor text", failure.Message);
        Assert.DoesNotContain("raw vendor text", failure.Hint);
    }

    [Fact]
    public void A_zero_quota_is_told_apart_from_being_throttled()
    {
        // Both come back as 429, but they need different things done about
        // them: one is "wait", the other is "enable billing". This is the exact
        // wording the API returned for a free-tier key on 2026-09-03.
        const string freeTier =
            "You exceeded your current quota. * Quota exceeded for metric: "
            + "generativelanguage.googleapis.com/generate_content_free_tier_requests, "
            + "limit: 0, model: gemini-3.1-flash-image";

        Assert.True(GeminiTextureProvider.QuotaIsZero(freeTier));
        Assert.False(GeminiTextureProvider.QuotaIsZero("Too many requests, retry in 13s."));

        var zero = GeminiTextureProvider.Describe(new Google.GenAI.ClientError(freeTier, 429, "RESOURCE_EXHAUSTED"));
        Assert.Equal("ai_quota_zero", zero.Code);
        Assert.Contains("billing", zero.Hint);
        Assert.Contains("Waiting will not help", zero.Hint);

        var throttled = GeminiTextureProvider.Describe(
            new Google.GenAI.ClientError("Too many requests, retry in 13s.", 429, "RESOURCE_EXHAUSTED"));
        Assert.Equal("ai_rate_limited", throttled.Code);
    }

    [Fact]
    public void A_network_failure_says_so_rather_than_blaming_the_prompt()
    {
        var failure = GeminiTextureProvider.Describe(new HttpRequestException("no route"));

        Assert.Equal("ai_network", failure.Code);
        Assert.Contains("could not be reached", failure.Message);
    }

    // ------------------------------------------------------------------
    // image probing
    // ------------------------------------------------------------------

    [Fact]
    public void A_png_header_gives_up_its_dimensions()
    {
        var png = new byte[24];
        new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(png, 0);
        Encoding.ASCII.GetBytes("IHDR").CopyTo(png, 12);
        // 2048 x 1024, big-endian.
        new byte[] { 0, 0, 0x08, 0x00 }.CopyTo(png, 16);
        new byte[] { 0, 0, 0x04, 0x00 }.CopyTo(png, 20);

        Assert.Equal((2048, 1024), ImageProbe.Measure(png));
    }

    [Fact]
    public void Something_that_is_not_an_image_measures_zero_rather_than_throwing()
        => Assert.Equal((0, 0), ImageProbe.Measure(Encoding.ASCII.GetBytes("not an image")));

    [Theory]
    [InlineData("image/png", ".png")]
    [InlineData("image/jpeg", ".jpg")]
    [InlineData("image/webp", ".webp")]
    [InlineData("image/tiff", null)]
    [InlineData(null, null)]
    public void Only_formats_the_editor_can_load_get_an_extension(string? mime, string? expected)
        => Assert.Equal(expected, ImageProbe.ExtensionFor(mime));

    // ------------------------------------------------------------------
    // the key never leaves the host
    // ------------------------------------------------------------------

    [Fact]
    public void The_stored_key_is_encrypted_on_disk_and_absent_from_what_the_interface_sees()
    {
        var settings = NoKeySettings();
        const string plaintext = "AIzaSy-not-a-real-key-0123456789";

        settings.SetAiApiKey(plaintext);

        // Encrypted, not merely encoded: the plaintext is nowhere in the field.
        Assert.True(settings.HasAiApiKey);
        Assert.NotNull(settings.Current.AiApiKeyProtected);
        Assert.DoesNotContain(plaintext, settings.Current.AiApiKeyProtected);

        // Readable again by this user, for an outbound request only.
        Assert.Equal(plaintext, settings.RevealAiApiKey());

        // And nothing of it -- not even the ciphertext -- is in the payload the
        // interface receives.
        var visible = settings.PublicView().ToJsonString();
        Assert.DoesNotContain(plaintext, visible);
        Assert.DoesNotContain(settings.Current.AiApiKeyProtected!, visible);
        Assert.DoesNotContain("aiApiKeyProtected", visible);

        // The rest of the settings still come through.
        Assert.Contains("\"theme\"", visible);
        Assert.Contains("\"aiProvider\"", visible);

        settings.SetAiApiKey(null);
    }

    [Fact]
    public void A_key_in_the_environment_is_used_and_is_reported_by_source_not_by_value()
    {
        var settings = NoKeySettings();
        Environment.SetEnvironmentVariable(AiKeyResolver.EnvVariable, "env-key-value");
        try
        {
            var status = new GeminiTextureProvider(settings, new LogService()).Status;

            Assert.True(status.Configured);
            Assert.Equal("environment", status.KeySource);

            // The status object is what crosses the bridge. It must carry no
            // trace of the key.
            var serialised = System.Text.Json.JsonSerializer.Serialize(status);
            Assert.DoesNotContain("env-key-value", serialised);
        }
        finally
        {
            Environment.SetEnvironmentVariable(AiKeyResolver.EnvVariable, null);
        }
    }

    // ------------------------------------------------------------------
    // registry
    // ------------------------------------------------------------------

    [Fact]
    public void Only_gemini_is_registered_and_a_stale_setting_does_not_disable_it()
    {
        var settings = NoKeySettings();
        settings.Update(s => s.AiProvider = "openai");

        var registry = new AiProviderRegistry(settings, new LogService());

        Assert.Equal(new[] { "gemini" }, registry.Ids);
        Assert.Equal("gemini", registry.Image.Id);
        Assert.Null(registry.Get("openai"));
    }

    // ------------------------------------------------------------------
    // helpers
    // ------------------------------------------------------------------

    /// <summary>
    /// A settings service with no key in it.
    /// </summary>
    /// <remarks>
    /// The data root is already redirected to a scratch folder for the whole
    /// test assembly (see <see cref="TestDataRoot"/>), so this only has to
    /// clear the two places a real key could still come from: the developer's
    /// own environment variable, and a key left behind by an earlier test.
    /// </remarks>
    private static SettingsService NoKeySettings()
    {
        Environment.SetEnvironmentVariable(AiKeyResolver.EnvVariable, null);
        var settings = new SettingsService(new LogService());
        settings.SetAiApiKey(null);
        return settings;
    }

    private static AiInputImage Png(AiImageRole role) =>
        new(role, new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G' }, "image/png");

    private static AiTextureRequest Request(
        string prompt, AiTextureMode mode, params AiInputImage[] images) =>
        new(
            Prompt: prompt,
            Mode: mode,
            Resolution: "2K",
            AspectRatio: "1:1",
            Images: images,
            Context: new AiGarmentContext("jbib", "Jacket / torso", 3, "Leather Jacket", true, 512));
}

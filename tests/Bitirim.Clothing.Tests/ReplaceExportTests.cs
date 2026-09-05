using Bitirim.Clothing.Core.Naming;
using Bitirim.Clothing.Core.Rage;
using Bitirim.Clothing.Export;
using Bitirim.Clothing.FiveFury;
using Bitirim.Clothing.Textures;
using Xunit;

namespace Bitirim.Clothing.Tests;

/// <summary>
/// Re-skinning a garment the game already ships.
/// </summary>
/// <remarks>
/// The shape guard is the reason most of these exist. The backend keeps the
/// source file's RSC7 header and swaps pixels inside it, which is only valid
/// while the replacement occupies exactly the same space. Writing a fresh
/// container instead was measured killing the D3D driver in game, so a
/// mismatch has to be refused here rather than shipped.
/// </remarks>
public sealed class ReplaceExportTests : IDisposable
{
    private readonly string _output;

    public ReplaceExportTests()
    {
        _output = Path.Combine(Path.GetTempPath(), "bcc_repl_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_output);
    }

    public void Dispose()
    {
        try { Directory.Delete(_output, recursive: true); } catch { /* best effort */ }
    }

    private static FiveFuryRageAssetBackend Backend() =>
        new(FixturePaths.PythonExe, FixturePaths.AssetService);

    /// <summary>Encodes a solid colour at the given size, in the given format.</summary>
    private static EncodedTexture Encode(int size, string format, System.Drawing.Color colour)
    {
        var png = Path.Combine(Path.GetTempPath(), $"bcc_repl_{Guid.NewGuid():N}.png");
        using (var bitmap = new System.Drawing.Bitmap(size, size))
        {
            using var graphics = System.Drawing.Graphics.FromImage(bitmap);
            graphics.Clear(colour);
            bitmap.Save(png, System.Drawing.Imaging.ImageFormat.Png);
        }

        try { return new BcnTextureEncoder().EncodeFile(png, format); }
        finally { try { File.Delete(png); } catch { /* temp */ } }
    }

    // ---------------------------------------------------------------- naming

    [Fact]
    public void REPLACE_NAMES_a_base_ped_garment_without_a_dlc_segment()
    {
        Assert.Equal(
            "mp_m_freemode_01",
            ReplaceResourceBuilder.StreamPrefix(ClothingNames.MaleFreemodePed, null));

        Assert.Equal(
            "mp_m_freemode_01^jbib_diff_003_c_uni",
            ReplaceResourceBuilder.StreamFileName(
                ClothingNames.MaleFreemodePed, null, "jbib_diff_003_c_uni"));
    }

    [Fact]
    public void REPLACE_NAMES_a_dlc_garment_with_the_dlc_folder_in_the_prefix()
    {
        // Measured in game: the streamer resolves the caret to a folder
        // separator, and this is exactly how the game's own DLC assets are
        // addressed -- mp_m_freemode_01_mp_m_valentines_02/jbib_diff_000_a_uni
        // answered a texture-dictionary probe, while the caret spelling did not.
        Assert.Equal(
            "mp_m_freemode_01_mp_m_valentines_02^jbib_diff_000_a_uni",
            ReplaceResourceBuilder.StreamFileName(
                ClothingNames.MaleFreemodePed, "mp_m_valentines_02", "jbib_diff_000_a_uni"));
    }

    [Fact]
    public async Task REPLACE_REJECTS_the_same_slot_letter_twice()
    {
        var texture = new EncodedTexture(new byte[16], 4, 4, "BC1", 1);
        var request = new ReplaceExportRequest(
            "bcc_dup", ClothingNames.MaleFreemodePed, null, PedComponent.Jbib, 3,
            new[]
            {
                new ReplaceSlot('a', "ignored.ytd", texture),
                new ReplaceSlot('a', "ignored.ytd", texture),
            },
            _output);

        var builder = new ReplaceResourceBuilder(new ThrowingBackend());
        var error = await Assert.ThrowsAsync<ArgumentException>(() => builder.BuildAsync(request));
        Assert.Contains("repeat", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task REPLACE_REJECTS_an_export_with_no_colours()
    {
        var request = new ReplaceExportRequest(
            "bcc_empty", ClothingNames.MaleFreemodePed, null, PedComponent.Jbib, 3,
            Array.Empty<ReplaceSlot>(), _output);

        var builder = new ReplaceResourceBuilder(new ThrowingBackend());
        await Assert.ThrowsAsync<ArgumentException>(() => builder.BuildAsync(request));
    }

    // ---------------------------------------------------------------- build

    [RequiresFixturesFact]
    public async Task REPLACE_EXPORT_writes_textures_a_manifest_and_a_sidecar_only()
    {
        await using var backend = Backend();
        var builder = new ReplaceResourceBuilder(backend);

        var source = await backend.ReadYtdAsync(FixturePaths.YtdA);
        var existing = source.Textures[0];
        var texture = Encode(existing.Width, existing.Format, System.Drawing.Color.Firebrick);

        var result = await builder.BuildAsync(new ReplaceExportRequest(
            "bcc_shop3", ClothingNames.MaleFreemodePed, null, PedComponent.Jbib, 3,
            new[] { new ReplaceSlot('a', FixturePaths.YtdA, texture) },
            _output,
            ArmsDrawable: 1));

        var names = result.Files.Select(Path.GetFileName).ToList();
        Assert.Contains("mp_m_freemode_01^jbib_diff_003_a_uni.ytd", names);
        Assert.Contains("fxmanifest.lua", names);
        Assert.Contains("garment.json", names);

        // Re-skinning never touches geometry, so no drawable is emitted and the
        // game keeps its own mesh.
        Assert.DoesNotContain(names, n => n!.EndsWith(".ydd", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, n => n!.EndsWith(".ymt", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, n => n!.EndsWith(".meta", StringComparison.OrdinalIgnoreCase));
    }

    [RequiresFixturesFact]
    public async Task REPLACE_MANIFEST_declares_neither_files_nor_data_file()
    {
        await using var backend = Backend();
        var builder = new ReplaceResourceBuilder(backend);

        var source = await backend.ReadYtdAsync(FixturePaths.YtdA);
        var existing = source.Textures[0];
        var texture = Encode(existing.Width, existing.Format, System.Drawing.Color.SteelBlue);

        var result = await builder.BuildAsync(new ReplaceExportRequest(
            "bcc_manifest", ClothingNames.MaleFreemodePed, null, PedComponent.Jbib, 3,
            new[] { new ReplaceSlot('a', FixturePaths.YtdA, texture) },
            _output));

        var manifest = await File.ReadAllTextAsync(
            Path.Combine(result.ResourceDirectory, "fxmanifest.lua"));

        // Published packs that work carry neither. Declaring a data_file was
        // part of the addon route that measured failing.
        Assert.DoesNotContain("data_file", manifest);
        Assert.DoesNotContain("files {", manifest);
        Assert.Contains("fx_version 'cerulean'", manifest);

        // A byte-order mark makes FiveM refuse the whole resource:
        // "unexpected symbol near '<\239>'". Seen on a running server.
        foreach (var name in new[] { "fxmanifest.lua", "garment.json" })
        {
            var bytes = await File.ReadAllBytesAsync(
                Path.Combine(result.ResourceDirectory, name));
            Assert.False(
                bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
                $"{name} was written with a UTF-8 BOM.");
        }
    }

    [RequiresFixturesFact]
    public async Task REPLACE_SIDECAR_records_the_arms_pairing()
    {
        await using var backend = Backend();
        var builder = new ReplaceResourceBuilder(backend);

        var source = await backend.ReadYtdAsync(FixturePaths.YtdA);
        var existing = source.Textures[0];
        var texture = Encode(existing.Width, existing.Format, System.Drawing.Color.Goldenrod);

        var result = await builder.BuildAsync(new ReplaceExportRequest(
            "bcc_arms", ClothingNames.MaleFreemodePed, null, PedComponent.Jbib, 3,
            new[] { new ReplaceSlot('a', FixturePaths.YtdA, texture) },
            _output,
            ArmsDrawable: 1));

        var sidecar = await File.ReadAllTextAsync(
            Path.Combine(result.ResourceDirectory, "garment.json"));

        // The game holds no forced-component data for base-ped drawables, so
        // this file is the only place the pairing survives.
        Assert.Contains("\"armsDrawable\": 1", sidecar);
        Assert.Contains("\"drawable\": 3", sidecar);
        Assert.Contains("\"jbib\"", sidecar);

        // And the rule that makes the shop act on it: jbib 11 -> uppr 3, for
        // every colour (-1) of drawable 3.
        var sql = await File.ReadAllTextAsync(
            Path.Combine(result.ResourceDirectory, "compat.sql"));
        Assert.Contains("bitirim_clothing_compatibility_rules", sql);
        Assert.Contains("'11_3_tex-1__3_1_tex0'", sql);
        Assert.Contains("'verified'", sql);
        // Re-running an export must update the rule, not stack duplicates.
        Assert.Contains("DELETE FROM", sql);
    }

    [RequiresFixturesFact]
    public async Task REPLACE_SIDECAR_numbers_a_slot_by_its_letter_not_by_its_position()
    {
        await using var backend = Backend();
        var builder = new ReplaceResourceBuilder(backend);

        var b = (await backend.ReadYtdAsync(FixturePaths.YtdB)).Textures[0];

        // One slot, and it is 'b'. In game that is texture 1. Numbering it 0
        // because it is the only entry in the package would send anyone
        // reading this file -- or writing a compatibility rule from it -- to
        // the wrong colour.
        var result = await builder.BuildAsync(new ReplaceExportRequest(
            "bcc_slot_b", ClothingNames.MaleFreemodePed, null, PedComponent.Jbib, 3,
            new[]
            {
                new ReplaceSlot('b', FixturePaths.YtdB,
                    Encode(b.Width, b.Format, System.Drawing.Color.SeaGreen)),
            },
            _output));

        var sidecar = await File.ReadAllTextAsync(
            Path.Combine(result.ResourceDirectory, "garment.json"));

        Assert.Contains("\"variant\": \"b\"", sidecar);
        Assert.Contains("\"textureIndex\": 1", sidecar);
        Assert.DoesNotContain("\"textureIndex\": 0", sidecar);
    }

    [Theory]
    [InlineData('a', 0)]
    [InlineData('b', 1)]
    [InlineData('z', 25)]
    public void A_slot_letter_and_its_texture_number_are_inverses(char letter, int index)
    {
        Assert.Equal(index, ClothingNames.VariantIndex(letter));
        Assert.Equal(letter, ClothingNames.VariantLetter(index));
    }

    [RequiresFixturesFact]
    public async Task REPLACE_WRITES_no_compat_rule_when_arms_are_left_alone()
    {
        await using var backend = Backend();
        var builder = new ReplaceResourceBuilder(backend);

        var existing = (await backend.ReadYtdAsync(FixturePaths.YtdA)).Textures[0];
        var result = await builder.BuildAsync(new ReplaceExportRequest(
            "bcc_noarms", ClothingNames.MaleFreemodePed, null, PedComponent.Jbib, 3,
            new[]
            {
                new ReplaceSlot('a', FixturePaths.YtdA,
                    Encode(existing.Width, existing.Format, System.Drawing.Color.Gray)),
            },
            _output));

        // No pairing chosen means no claim about arms, so nothing to run.
        Assert.False(File.Exists(Path.Combine(result.ResourceDirectory, "compat.sql")));
        Assert.DoesNotContain(result.Files, f => f.EndsWith("compat.sql", StringComparison.Ordinal));
    }

    [RequiresFixturesFact]
    public async Task REPLACE_EXPORT_writes_each_colour_into_its_own_slot_file()
    {
        await using var backend = Backend();
        var builder = new ReplaceResourceBuilder(backend);

        // The two fixture slots genuinely differ -- a is BC3, b is BC1 -- which
        // is the whole reason a colour has to go into its own slot's file
        // rather than a shared one.
        var a = (await backend.ReadYtdAsync(FixturePaths.YtdA)).Textures[0];
        var b = (await backend.ReadYtdAsync(FixturePaths.YtdB)).Textures[0];

        var result = await builder.BuildAsync(new ReplaceExportRequest(
            "bcc_two", ClothingNames.MaleFreemodePed, null, PedComponent.Jbib, 3,
            new[]
            {
                new ReplaceSlot('a', FixturePaths.YtdA,
                    Encode(a.Width, a.Format, System.Drawing.Color.Firebrick)),
                new ReplaceSlot('b', FixturePaths.YtdB,
                    Encode(b.Width, b.Format, System.Drawing.Color.SeaGreen)),
            },
            _output));

        var names = result.Files.Select(Path.GetFileName).ToList();
        Assert.Contains("mp_m_freemode_01^jbib_diff_003_a_uni.ytd", names);
        Assert.Contains("mp_m_freemode_01^jbib_diff_003_b_uni.ytd", names);

        // Each written file keeps the shape of the slot it came from, and the
        // lookup name the drawable's sampler asks for.
        foreach (var (variant, source) in new[] { ('a', FixturePaths.YtdA), ('b', FixturePaths.YtdB) })
        {
            var written = Path.Combine(
                result.ResourceDirectory, "stream",
                $"mp_m_freemode_01^jbib_diff_003_{variant}_uni.ytd");
            var after = (await backend.ReadYtdAsync(written)).Textures[0];
            var before = (await backend.ReadYtdAsync(source)).Textures[0];

            Assert.Equal(before.Name, after.Name);
            Assert.Equal(before.Format, after.Format);
            Assert.Equal(before.Width, after.Width);
            Assert.Equal(before.MipCount, after.MipCount);
            Assert.NotEqual(before.DataSha, after.DataSha);
        }
    }

    [Fact]
    public void REPLACE_IS_the_default_mode_for_a_new_project()
    {
        // Addon is still selectable, but it has never been seen working, so it
        // is not what a project starts on.
        Assert.Equal("replace", new Bitirim.Clothing.Editor.Projects.ExportSettings().Mode);
    }

    [RequiresFixturesFact]
    public async Task REPLACE_REFUSES_a_texture_that_does_not_fit_its_slot()
    {
        await using var backend = Backend();
        var builder = new ReplaceResourceBuilder(backend);

        var source = await backend.ReadYtdAsync(FixturePaths.YtdA);
        var existing = source.Textures[0];
        Assert.True(existing.Width >= 256, "fixture is expected to be larger than the mismatch");

        // Half the size: same format, wrong footprint. Writing this would need
        // a rebuilt container, which is the thing that crashes the driver.
        var tooSmall = Encode(existing.Width / 2, existing.Format, System.Drawing.Color.Firebrick);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            builder.BuildAsync(new ReplaceExportRequest(
                "bcc_mismatch", ClothingNames.MaleFreemodePed, null, PedComponent.Jbib, 3,
                new[] { new ReplaceSlot('a', FixturePaths.YtdA, tooSmall) },
                _output)));

        Assert.Contains("does not fit", error.Message);
        Assert.Contains("'a'", error.Message);
    }

    // ------------------------------------------------------------ shop images

    [RequiresFixturesFact]
    public async Task SHOP_IMAGES_are_drawn_with_the_names_the_shop_looks_for()
    {
        await using var backend = Backend();

        var blobPath = Path.Combine(_output, "mesh.bin");
        var mesh = await backend.ExtractMeshAsync(FixturePaths.Ydd, blobPath);
        var blob = await File.ReadAllBytesAsync(mesh.Blob);

        var designs = new List<string>();
        foreach (var colour in new[]
                 {
                     System.Drawing.Color.Firebrick, System.Drawing.Color.SteelBlue,
                 })
        {
            var png = Path.Combine(_output, $"design_{designs.Count}.png");
            using (var bitmap = new System.Drawing.Bitmap(256, 256))
            {
                using var graphics = System.Drawing.Graphics.FromImage(bitmap);
                graphics.Clear(colour);
                bitmap.Save(png, System.Drawing.Imaging.ImageFormat.Png);
            }
            designs.Add(png);
        }

        var result = ShopImageWriter.Write(new ShopImageRequest(
            _output, PedComponent.Jbib, 3, blob, mesh.ByteLayout, designs));

        Assert.Null(result.SkippedBecause);

        // The shop reads images/{slot}_{drawable}.png for the card and
        // images/tex/{slot}_{drawable}_{texture}.png for each colour. jbib is
        // "jacket" there, from its config.
        var root = Path.Combine(_output, ShopImageWriter.FolderName);
        Assert.True(File.Exists(Path.Combine(root, "jacket_3.png")));
        Assert.True(File.Exists(Path.Combine(root, "tex", "jacket_3_0.png")));
        Assert.True(File.Exists(Path.Combine(root, "tex", "jacket_3_1.png")));

        // One card, one swatch per colour -- no swatch for a colour that is not there.
        Assert.False(File.Exists(Path.Combine(root, "tex", "jacket_3_2.png")));
    }

    [Fact]
    public void SHOP_IMAGES_are_skipped_for_a_component_the_shop_has_no_category_for()
    {
        // Beards have no shop category, so there is no name to write under.
        // Saying so beats writing a file the shop will never read.
        var result = ShopImageWriter.Write(new ShopImageRequest(
            _output, PedComponent.Berd, 0, Array.Empty<byte>(),
            new Dictionary<string, int[]>(), new[] { "unused.png" }));

        Assert.Empty(result.Files);
        Assert.Contains("berd", result.SkippedBecause);
    }

    /// <summary>Fails loudly if a test reaches the backend it should not.</summary>
    private sealed class ThrowingBackend : IRageAssetBackend
    {
        private static Exception Unexpected([System.Runtime.CompilerServices.CallerMemberName] string member = "")
            => new InvalidOperationException($"{member} should not have been reached.");

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public Task<BackendCapabilities> GetCapabilitiesAsync(CancellationToken ct = default) => throw Unexpected();
        public Task<DrawableDictionaryInfo> ReadYddAsync(string path, CancellationToken ct = default) => throw Unexpected();
        public Task<TextureDictionaryInfo> ReadYtdAsync(string path, CancellationToken ct = default) => throw Unexpected();
        public Task<PedMetadataInfo> ReadYmtAsync(string path, CancellationToken ct = default) => throw Unexpected();
        public Task<MeshInfo> ExtractMeshAsync(string path, string blobPath, string lod = "high", CancellationToken ct = default) => throw Unexpected();
        public Task<TextureInfo> DecodeTextureAsync(string path, string blobPath, int index = 0, CancellationToken ct = default) => throw Unexpected();
        public Task<WriteResult> ReplaceYtdTextureAsync(string sourcePath, string destinationPath, EncodedTexture texture, string? textureName = null, CancellationToken ct = default) => throw Unexpected();
        public Task<WriteResult> MakeEmissiveAsync(string sourcePath, string destinationPath, double multiplier = 1.0, CancellationToken ct = default) => throw Unexpected();
        public Task<WriteResult> BuildAddonYmtAsync(string templatePath, string destinationPath, int component, int textureCount, uint dlcNameHash, CancellationToken ct = default) => throw Unexpected();
        public Task<PackValidationResult> ValidatePackAsync(IReadOnlyList<string> files, CancellationToken ct = default) => throw Unexpected();
    }
}

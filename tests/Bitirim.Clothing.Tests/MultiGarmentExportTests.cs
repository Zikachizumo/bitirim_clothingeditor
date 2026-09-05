using Bitirim.Clothing.Core.Naming;
using Bitirim.Clothing.Export;
using Bitirim.Clothing.FiveFury;
using Bitirim.Clothing.Textures;
using Bitirim.Clothing.Validation;
using Xunit;

namespace Bitirim.Clothing.Tests;

/// <summary>
/// Building a resource that carries more than one garment.
/// </summary>
/// <remarks>
/// Runs against the real fixtures and the real backend, and re-parses
/// everything it writes. What it does <em>not</em> do is prove the game loads
/// the result -- no test in this repository can, until the in-game step is done.
/// </remarks>
public sealed class MultiGarmentExportTests : IDisposable
{
    private readonly string _output;

    public MultiGarmentExportTests()
    {
        _output = Path.Combine(Path.GetTempPath(), "bcc_multi_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_output);
    }

    public void Dispose()
    {
        try { Directory.Delete(_output, recursive: true); } catch { /* best effort */ }
    }

    private static FiveFuryRageAssetBackend Backend() =>
        new(FixturePaths.PythonExe, FixturePaths.AssetService);

    /// <summary>A BC3-encoded slot built from the fixture's own diffuse.</summary>
    private static TextureSlot Slot(char variant)
    {
        var encoder = new BcnTextureEncoder();
        // Encode from a solid PNG so the test does not depend on a decode step.
        var png = Path.Combine(Path.GetTempPath(), $"bcc_slot_{variant}_{Guid.NewGuid():N}.png");
        using (var bitmap = new System.Drawing.Bitmap(512, 512))
        {
            using var graphics = System.Drawing.Graphics.FromImage(bitmap);
            graphics.Clear(variant == 'a' ? System.Drawing.Color.Firebrick
                                          : System.Drawing.Color.SteelBlue);
            bitmap.Save(png, System.Drawing.Imaging.ImageFormat.Png);
        }

        try
        {
            return new TextureSlot(variant, png, encoder.EncodeFile(png, "BC3"));
        }
        finally
        {
            try { File.Delete(png); } catch { /* temp */ }
        }
    }

    [RequiresFixturesFact]
    public async Task MULTI_EXPORT_TEST_writes_one_dlc_per_garment()
    {
        await using var backend = Backend();
        var builder = new AddonResourceBuilder(backend, new ValidationEngine());

        var request = new MultiAddonExportRequest(
            ResourceName: "bcc_pack_test",
            Ped: ClothingNames.MaleFreemodePed,
            PackDlcName: "bcc_pack",
            YmtTemplatePath: FixturePaths.Ymt,
            Garments: new[]
            {
                new AddonGarment("Jacket", PedComponent.Jbib, 0,
                    FixturePaths.Ydd, FixturePaths.YtdA, new[] { Slot('a') }),
                new AddonGarment("Second", PedComponent.Jbib, 1,
                    FixturePaths.Ydd, FixturePaths.YtdA, new[] { Slot('a'), Slot('b') }),
            },
            OutputDirectory: _output);

        var result = await builder.BuildManyAsync(request);

        var names = result.Files.Select(Path.GetFileName).ToList();

        // Each garment gets its own DLC identity, so the two metadata files
        // cannot overwrite each other.
        Assert.Contains("mp_m_freemode_01_bcc_pack_jbib_000.ymt", names);
        Assert.Contains("mp_m_freemode_01_bcc_pack_jbib_001.ymt", names);

        // One drawable and one texture dictionary per variation, per garment.
        Assert.Contains("mp_m_freemode_01_bcc_pack_jbib_000^jbib_000_u.ydd", names);
        Assert.Contains("mp_m_freemode_01_bcc_pack_jbib_001^jbib_000_u.ydd", names);
        Assert.Contains("mp_m_freemode_01_bcc_pack_jbib_000^jbib_diff_000_a_uni.ytd", names);
        Assert.Contains("mp_m_freemode_01_bcc_pack_jbib_001^jbib_diff_000_a_uni.ytd", names);
        Assert.Contains("mp_m_freemode_01_bcc_pack_jbib_001^jbib_diff_000_b_uni.ytd", names);
    }

    [RequiresFixturesFact]
    public async Task MULTI_EXPORT_TEST_every_written_file_re_parses()
    {
        await using var backend = Backend();
        var builder = new AddonResourceBuilder(backend, new ValidationEngine());

        var result = await builder.BuildManyAsync(new MultiAddonExportRequest(
            ResourceName: "bcc_readback",
            Ped: ClothingNames.MaleFreemodePed,
            PackDlcName: "bcc_rb",
            YmtTemplatePath: FixturePaths.Ymt,
            Garments: new[]
            {
                new AddonGarment("A", PedComponent.Jbib, 0,
                    FixturePaths.Ydd, FixturePaths.YtdA, new[] { Slot('a') }),
                new AddonGarment("B", PedComponent.Uppr, 3,
                    FixturePaths.Ydd, FixturePaths.YtdA, new[] { Slot('a') }),
            },
            OutputDirectory: _output));

        Assert.True(result.ReadBack.AllOk,
            "written files that fail to re-parse: " + string.Join(", ",
                result.ReadBack.Files.Where(f => !f.Ok).Select(f => $"{Path.GetFileName(f.File)} ({f.Error})")));
    }

    [RequiresFixturesFact]
    public async Task MULTI_EXPORT_TEST_the_manifest_lists_every_metadata_file()
    {
        await using var backend = Backend();
        var builder = new AddonResourceBuilder(backend, new ValidationEngine());

        var result = await builder.BuildManyAsync(new MultiAddonExportRequest(
            ResourceName: "bcc_manifest",
            Ped: ClothingNames.MaleFreemodePed,
            PackDlcName: "bcc_mf",
            YmtTemplatePath: FixturePaths.Ymt,
            Garments: new[]
            {
                new AddonGarment("A", PedComponent.Jbib, 0,
                    FixturePaths.Ydd, FixturePaths.YtdA, new[] { Slot('a') }),
                new AddonGarment("B", PedComponent.Lowr, 2,
                    FixturePaths.Ydd, FixturePaths.YtdA, new[] { Slot('a') }),
            },
            OutputDirectory: _output,
            ManifestMode: ManifestMode.DataFile));

        var manifest = await File.ReadAllTextAsync(
            Path.Combine(result.ResourceDirectory, "fxmanifest.lua"));

        Assert.Contains("mp_m_freemode_01_bcc_mf_jbib_000.ymt", manifest);
        Assert.Contains("mp_m_freemode_01_bcc_mf_lowr_002.ymt", manifest);

        // DataFile mode declares each metadata file explicitly.
        Assert.Equal(2, manifest.Split("data_file 'DLC_ITYP_REQUEST'").Length - 1);
    }

    [RequiresFixturesFact]
    public async Task SINGLE_EXPORT_TEST_keeps_the_dlc_name_exactly_as_asked()
    {
        await using var backend = Backend();
        var builder = new AddonResourceBuilder(backend, new ValidationEngine());

        // v0.1.0 wrote <ped>_<dlc>.ymt with no suffix; a one-garment export
        // must keep producing exactly that.
        var result = await builder.BuildAsync(new AddonExportRequest(
            ResourceName: "bcc_single",
            Ped: ClothingNames.MaleFreemodePed,
            DlcName: "bcc_m_jbib_single",
            Component: PedComponent.Jbib,
            DrawableIndex: 0,
            SourceYddPath: FixturePaths.Ydd,
            SourceYtdPath: FixturePaths.YtdA,
            YmtTemplatePath: FixturePaths.Ymt,
            Slots: new[] { Slot('a') },
            OutputDirectory: _output));

        var names = result.Files.Select(Path.GetFileName).ToList();

        Assert.Contains("mp_m_freemode_01_bcc_m_jbib_single.ymt", names);
        Assert.Contains("mp_m_freemode_01_bcc_m_jbib_single^jbib_000_u.ydd", names);
        Assert.DoesNotContain(names, n => n!.Contains("_jbib_000."));
    }

    [RequiresFixturesFact]
    public async Task MULTI_EXPORT_TEST_the_drawable_is_copied_byte_for_byte()
    {
        await using var backend = Backend();
        var builder = new AddonResourceBuilder(backend, new ValidationEngine());

        var result = await builder.BuildManyAsync(new MultiAddonExportRequest(
            ResourceName: "bcc_bytes",
            Ped: ClothingNames.MaleFreemodePed,
            PackDlcName: "bcc_by",
            YmtTemplatePath: FixturePaths.Ymt,
            Garments: new[]
            {
                new AddonGarment("A", PedComponent.Jbib, 0,
                    FixturePaths.Ydd, FixturePaths.YtdA, new[] { Slot('a') }),
            },
            OutputDirectory: _output));

        var written = result.Files.First(f => f.EndsWith(".ydd", StringComparison.Ordinal));

        // The whole reason the writer is disabled: the drawable must arrive
        // unchanged, bounding volume included.
        Assert.Equal(
            await File.ReadAllBytesAsync(FixturePaths.Ydd),
            await File.ReadAllBytesAsync(written));
    }

    [RequiresFixturesFact]
    public async Task MULTI_EXPORT_TEST_a_garment_with_no_slots_is_refused()
    {
        await using var backend = Backend();
        var builder = new AddonResourceBuilder(backend, new ValidationEngine());

        var request = new MultiAddonExportRequest(
            ResourceName: "bcc_empty",
            Ped: ClothingNames.MaleFreemodePed,
            PackDlcName: "bcc_e",
            YmtTemplatePath: FixturePaths.Ymt,
            Garments: new[]
            {
                new AddonGarment("A", PedComponent.Jbib, 0,
                    FixturePaths.Ydd, FixturePaths.YtdA, Array.Empty<TextureSlot>()),
            },
            OutputDirectory: _output);

        await Assert.ThrowsAsync<ArgumentException>(() => builder.BuildManyAsync(request));
    }

    [Fact]
    public void MULTI_EXPORT_TEST_dlc_names_are_derived_per_component_and_index()
    {
        var jacket = new AddonGarment("Jacket", PedComponent.Jbib, 4,
            "a.ydd", "a.ytd", Array.Empty<TextureSlot>());
        var trousers = new AddonGarment("Trousers", PedComponent.Lowr, 12,
            "b.ydd", "b.ytd", Array.Empty<TextureSlot>());

        Assert.Equal("mypack_jbib_004", jacket.DlcNameFor("mypack"));
        Assert.Equal("mypack_lowr_012", trousers.DlcNameFor("mypack"));
    }
}

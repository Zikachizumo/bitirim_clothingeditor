using Bitirim.Clothing.Core.Naming;
using Bitirim.Clothing.Core.Rage;
using Bitirim.Clothing.FiveFury;
using Xunit;

namespace Bitirim.Clothing.Tests;

/// <summary>
/// Format-level tests against real RAGE assets.
/// </summary>
/// <remarks>
/// These assert on <em>structure</em>, not bytes. The writer legitimately
/// produces different compression and field ordering, so byte equality is the
/// wrong bar; what must survive a round-trip is the data the game reads.
/// </remarks>
public sealed class RageFormatTests
{
    private static FiveFuryRageAssetBackend Backend() =>
        new(FixturePaths.PythonExe, FixturePaths.AssetService);

    [RequiresFixturesFact]
    public async Task Backend_reports_capabilities_honestly()
    {
        await using var backend = Backend();
        var caps = await backend.GetCapabilitiesAsync();

        Assert.Equal("fivefury", caps.Backend);
        Assert.True(caps.ReadYdd);
        Assert.True(caps.ReadYtd);
        Assert.True(caps.ReadYmt);
        Assert.True(caps.WriteYtd);
        Assert.True(caps.WriteYmt);

        // Deliberately false: the writer recomputes the bounding volume of a
        // skinned ped drawable incorrectly, so the export path copies the
        // source YDD instead. If this ever flips to true, the change must be
        // justified by a passing bounding-box test.
        Assert.False(caps.WriteYdd);
        Assert.False(caps.WriteYddGeometry);
    }

    // ---------------- YTD ----------------

    [RequiresFixturesFact]
    public async Task YTD_READ_TEST_reads_a_real_clothing_texture_dictionary()
    {
        await using var backend = Backend();
        var ytd = await backend.ReadYtdAsync(FixturePaths.YtdA);

        Assert.Equal(1, ytd.TextureCount);
        var t = ytd.Textures[0];
        Assert.Equal("jbib_diff_000_a_uni", t.Name);
        Assert.Equal(512, t.Width);
        Assert.Equal(512, t.Height);
        Assert.Equal("BC3", t.Format);
        Assert.Equal(8, t.MipCount);
    }

    [RequiresFixturesFact]
    public async Task YTD_WRITE_TEST_round_trip_preserves_every_surface()
    {
        await using var backend = Backend();
        var before = await backend.ReadYtdAsync(FixturePaths.YtdA);

        // Re-encoding the source surface unchanged is the closest thing to a
        // pure round-trip the API allows, since replace is the only write path.
        var donor = before.Textures[0];
        var temp = NewTempFile(".ytd");
        try
        {
            var raw = await ExtractSurfaceAsync(backend, FixturePaths.YtdA);
            await backend.ReplaceYtdTextureAsync(
                FixturePaths.YtdA, temp,
                new EncodedTexture(raw, donor.Width, donor.Height, donor.Format, donor.MipCount));

            var after = await backend.ReadYtdAsync(temp);
            Assert.Equal(before.TextureCount, after.TextureCount);
            Assert.Equal(donor.Name, after.Textures[0].Name);
            Assert.Equal(donor.Width, after.Textures[0].Width);
            Assert.Equal(donor.Height, after.Textures[0].Height);
            Assert.Equal(donor.Format, after.Textures[0].Format);
            Assert.Equal(donor.MipCount, after.Textures[0].MipCount);
            Assert.Equal(donor.DataSha, after.Textures[0].DataSha);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    [RequiresFixturesFact]
    public async Task YTD_TEXTURE_REPLACE_TEST_swaps_pixels_but_keeps_the_lookup_name()
    {
        await using var backend = Backend();
        var target = await backend.ReadYtdAsync(FixturePaths.YtdA);
        var donorSurface = await ExtractSurfaceAsync(backend, FixturePaths.YtdB);
        var donor = (await backend.ReadYtdAsync(FixturePaths.YtdB)).Textures[0];

        var temp = NewTempFile(".ytd");
        try
        {
            // The container comes from the donor, not from the target. A YTD is
            // written by preserving its RSC7 header and swapping bytes inside
            // the body, so the file has to be the one whose shape already fits
            // the surface -- B's pixels are BC1 where A's slot is BC3, and
            // rebuilding a container to bridge that is what kills the D3D
            // driver in game. The name is what gets carried across instead.
            await backend.ReplaceYtdTextureAsync(
                FixturePaths.YtdB, temp,
                new EncodedTexture(donorSurface, donor.Width, donor.Height, donor.Format, donor.MipCount),
                textureName: target.Textures[0].Name);

            var after = await backend.ReadYtdAsync(temp);

            // The name the drawable's DiffuseSampler looks up must survive.
            Assert.Equal(target.Textures[0].Name, after.Textures[0].Name);
            // The pixels must actually have changed.
            Assert.NotEqual(target.Textures[0].DataSha, after.Textures[0].DataSha);
            Assert.Equal(donor.DataSha, after.Textures[0].DataSha);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    // ---------------- YDD ----------------

    [RequiresFixturesFact]
    public async Task YDD_READ_TEST_exposes_geometry_skinning_materials_and_embedded_textures()
    {
        await using var backend = Backend();
        var ydd = await backend.ReadYddAsync(FixturePaths.Ydd);

        Assert.Equal("jbib_000_u", ydd.Name);
        Assert.Equal(1, ydd.DrawableCount);

        var d = ydd.Drawables[0];

        // Three LODs, as freemode clothing requires.
        Assert.Contains("high", d.Lods.Keys);
        Assert.Contains("med", d.Lods.Keys);
        Assert.Contains("low", d.Lods.Keys);

        var high = d.Lods["high"];
        Assert.True(high.Vertices > 0);
        Assert.True(high.Indices > 0);
        Assert.All(high.Skinned, Assert.True);
        Assert.Equal(high.Vertices, high.BlendIndices);
        Assert.Equal(high.Vertices, high.BlendWeights);
        Assert.Equal(high.Vertices, high.Normals);

        // Ped shader family.
        Assert.All(d.Materials, m => Assert.StartsWith("ped", m.Shader, StringComparison.Ordinal));

        // The diffuse is external; normal and spec are embedded. This split is
        // the reason a texture-only workflow never has to write a YDD.
        Assert.Contains(d.EmbeddedTextures, t => t.Name == "jbib_normal_000");
        Assert.Contains(d.EmbeddedTextures, t => t.Name == "jbib_spec_000");
        Assert.DoesNotContain(d.EmbeddedTextures, t => t.Name == "jbib_diff_000_a_uni");
        Assert.Contains("jbib_diff_000_a_uni", d.TextureNames);
    }

    // ---------------- YMT ----------------

    [RequiresFixturesFact]
    public async Task YMT_READ_TEST_decodes_ped_variation_metadata()
    {
        await using var backend = Backend();
        var ymt = await backend.ReadYmtAsync(FixturePaths.Ymt);

        Assert.Equal("CPedVariationInfo", ymt.Root);
        Assert.Equal(12, ymt.AvailComp.Count);
        Assert.Equal(12, ymt.Components.Count);

        // JBIB is component 11 and the base male ped ships 16 jacket drawables.
        var jbib = ymt.Components[(int)PedComponent.Jbib];
        Assert.Equal(16, jbib.DrawableCount);
        Assert.True(jbib.NumAvailTex > 0);
    }

    [RequiresFixturesFact]
    public async Task YMT_WRITE_TEST_builds_a_single_component_addon()
    {
        await using var backend = Backend();
        var temp = NewTempFile(".ymt");
        try
        {
            await backend.BuildAddonYmtAsync(
                FixturePaths.Ymt, temp, (int)PedComponent.Jbib, textureCount: 3,
                dlcNameHash: Joaat.Hash("bcc_test"));

            var ymt = await backend.ReadYmtAsync(temp);

            Assert.Equal("CPedVariationInfo", ymt.Root);
            Assert.Single(ymt.Components);

            // Only JBIB is contributed; everything else is marked unused.
            Assert.Equal(12, ymt.AvailComp.Count);
            Assert.Equal(0, ymt.AvailComp[(int)PedComponent.Jbib]);
            for (var i = 0; i < 12; i++)
                if (i != (int)PedComponent.Jbib)
                    Assert.Equal(255, ymt.AvailComp[i]);

            var comp = ymt.Components[0];
            Assert.Equal(3, comp.NumAvailTex);
            Assert.Equal(1, comp.DrawableCount);
            Assert.Equal(3, comp.Drawables[0].Textures);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    // ---------------- helpers ----------------

    /// <summary>
    /// Reads a dictionary's raw compressed surface bytes.
    /// </summary>
    /// <remarks>
    /// The service contract reports surfaces by hash and size, not by content,
    /// because the product never needs to pull raw blocks back out. Rather than
    /// widen the contract for the tests' benefit, this shells out to the runtime
    /// directly -- test scaffolding, not a production path.
    /// </remarks>
    private static async Task<byte[]> ExtractSurfaceAsync(IRageAssetBackend backend, string ytdPath)
    {
        var info = await backend.ReadYtdAsync(ytdPath);
        var expected = info.Textures[0].DataBytes;
        var script = $"""
            import sys, fivefury as ff
            ytd = ff.read_ytd(r"{ytdPath}")
            sys.stdout.buffer.write(ytd.textures[0].data)
            """;
        var psi = new System.Diagnostics.ProcessStartInfo(FixturePaths.PythonExe)
        {
            RedirectStandardOutput = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add(script);

        using var proc = System.Diagnostics.Process.Start(psi)!;
        using var ms = new MemoryStream();
        await proc.StandardOutput.BaseStream.CopyToAsync(ms);
        await proc.WaitForExitAsync();

        var bytes = ms.ToArray();
        Assert.Equal(expected, bytes.LongLength);
        return bytes;
    }

    private static string NewTempFile(string ext) =>
        Path.Combine(Path.GetTempPath(), $"bcctest_{Guid.NewGuid():N}{ext}");
}

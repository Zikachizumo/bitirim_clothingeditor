using System.Drawing;
using System.Drawing.Imaging;
using System.Text.Json;
using Bitirim.Clothing.Core.Naming;
using Bitirim.Clothing.Export;
using Bitirim.Clothing.FiveFury;
using Bitirim.Clothing.Textures;
using Xunit;

namespace Bitirim.Clothing.Tests;

/// <summary>
/// Neon garments: the glow mask, and the drawable that carries it.
/// </summary>
/// <remarks>
/// Emission was measured in a running client to be the product of three
/// things -- the mesh's vertex-colour blue channel, the diffuse alpha, and the
/// shader's multiplier. These cover the two halves the app is responsible for.
/// The third, the shader itself, is the backend's and is exercised by the
/// fixture-backed test below.
/// </remarks>
public sealed class EmissiveTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "bcc-emissive-" + Guid.NewGuid().ToString("N")[..8]);

    public EmissiveTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private string Png(string name, Func<int, int, Color> pixel, int size = 16)
    {
        var path = Path.Combine(_dir, name);
        using var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
            bmp.SetPixel(x, y, pixel(x, y));
        bmp.Save(path, ImageFormat.Png);
        return path;
    }

    [Fact]
    public void A_GLOW_MASK_becomes_the_alpha_channel_and_leaves_colour_alone()
    {
        var design = Png("design.png", (_, _) => Color.FromArgb(255, 200, 40, 40));
        var mask = Png("mask.png", (x, _) => x < 8 ? Color.White : Color.Black);

        var merged = GlowMask.Apply(design, mask, Path.Combine(_dir, "merged.png"));

        using var result = new Bitmap(merged);
        var lit = result.GetPixel(2, 8);
        var dark = result.GetPixel(12, 8);

        Assert.Equal(255, lit.A);
        Assert.Equal(0, dark.A);

        // The garment's own colour is untouched on both sides: the mask says
        // where it glows, never what colour it is.
        Assert.Equal((200, 40, 40), (lit.R, lit.G, lit.B));
        Assert.Equal((200, 40, 40), (dark.R, dark.G, dark.B));
    }

    [Fact]
    public void GREY_IN_THE_MASK_glows_dimly_rather_than_snapping_to_on_or_off()
    {
        var design = Png("design.png", (_, _) => Color.White);
        var mask = Png("mask.png", (_, _) => Color.FromArgb(255, 128, 128, 128));

        var merged = GlowMask.Apply(design, mask, Path.Combine(_dir, "merged.png"));

        using var result = new Bitmap(merged);
        Assert.InRange(result.GetPixel(8, 8).A, 120, 136);
    }

    [Fact]
    public void AN_UNPAINTED_MASK_AREA_does_not_glow_even_though_it_is_not_black()
    {
        // A transparent pixel carries whatever colour happens to sit under it.
        // Reading its luminance alone would light up areas nobody painted.
        var design = Png("design.png", (_, _) => Color.White);
        var mask = Png("mask.png", (_, _) => Color.FromArgb(0, 255, 255, 255));

        var merged = GlowMask.Apply(design, mask, Path.Combine(_dir, "merged.png"));

        using var result = new Bitmap(merged);
        Assert.Equal(0, result.GetPixel(8, 8).A);
    }

    [Fact]
    public void NO_MASK_means_the_whole_garment_glows()
    {
        var design = Png("design.png", (_, _) => Color.FromArgb(30, 90, 200, 90));

        var merged = GlowMask.Apply(design, null, Path.Combine(_dir, "merged.png"));

        using var result = new Bitmap(merged);
        Assert.Equal(255, result.GetPixel(8, 8).A);
    }

    [Fact]
    public void A_MASK_OF_THE_WRONG_SIZE_is_refused_rather_than_stretched()
    {
        var design = Png("design.png", (_, _) => Color.White, size: 16);
        var mask = Png("mask.png", (_, _) => Color.White, size: 8);

        var error = Assert.Throws<InvalidOperationException>(
            () => GlowMask.Apply(design, mask, Path.Combine(_dir, "merged.png")));

        // Resizing would soften an edge the user drew at pixel precision, and
        // the glow's shape is exactly what that edge decides.
        Assert.Contains("8x8", error.Message);
        Assert.Contains("16x16", error.Message);
    }

    [RequiresFixturesFact]
    public async Task A_NEON_EXPORT_ships_the_garment_mesh_which_a_plain_reskin_never_does()
    {
        await using var backend = new FiveFuryRageAssetBackend(
            FixturePaths.PythonExe,
            Path.Combine(FixturePaths.RepoRoot, "src", "assetservice", "main.py"));

        var source = await backend.ReadYtdAsync(FixturePaths.YtdA);
        var existing = source.Textures[0];
        var encoded = new BcnTextureEncoder().EncodeFile(
            Png("flat.png", (_, _) => Color.White, existing.Width), existing.Format);

        var builder = new ReplaceResourceBuilder(backend);
        var result = await builder.BuildAsync(new ReplaceExportRequest(
            "bcc_neon_test", ClothingNames.MaleFreemodePed, null, PedComponent.Jbib, 0,
            new[] { new ReplaceSlot('a', FixturePaths.YtdA, encoded) },
            _dir,
            Emissive: new EmissiveRequest(FixturePaths.Ydd, 1.0)));

        var names = result.Files.Select(Path.GetFileName).ToList();
        Assert.Contains("mp_m_freemode_01^jbib_000_u.ydd", names);

        var sidecar = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(result.ResourceDirectory, "garment.json")));
        var emissive = sidecar.RootElement.GetProperty("emissive");
        Assert.Equal(1.0, emissive.GetProperty("multiplier").GetDouble());

        // Both halves of the effect have to be in the written file. A shader
        // swap on its own leaves the garment dark, because every vertex of a
        // base-ped mesh carries blue = 0 and the product collapses to zero.
        var written = Path.Combine(result.ResourceDirectory, "stream",
            "mp_m_freemode_01^jbib_000_u.ydd");
        var drawable = await backend.ReadYddAsync(written);
        Assert.Equal("ped_emissive", drawable.Drawables[0].Materials[0].Shader);
    }
}

using Bitirim.Clothing.Core.Naming;
using Bitirim.Clothing.Core.Rage;
using Bitirim.Clothing.Validation;
using Xunit;

namespace Bitirim.Clothing.Tests;

/// <summary>
/// Pure logic: no fixtures, no backend, no game required.
/// </summary>
public sealed class NamingTests
{
    [Theory]
    [InlineData(PedComponent.Head, 0, "head")]
    [InlineData(PedComponent.Berd, 1, "berd")]
    [InlineData(PedComponent.Uppr, 3, "uppr")]
    [InlineData(PedComponent.Lowr, 4, "lowr")]
    [InlineData(PedComponent.Feet, 6, "feet")]
    [InlineData(PedComponent.Teef, 7, "teef")]
    [InlineData(PedComponent.Accs, 8, "accs")]
    [InlineData(PedComponent.Decl, 10, "decl")]
    [InlineData(PedComponent.Jbib, 11, "jbib")]
    public void Component_indices_match_the_engine(PedComponent c, int index, string prefix)
    {
        Assert.Equal(index, (int)c);
        Assert.Equal(prefix, ClothingNames.Prefix(c));
    }

    [Fact]
    public void Drawable_names_are_zero_padded_to_three_digits()
    {
        Assert.Equal("jbib_000_u", ClothingNames.Drawable(PedComponent.Jbib, 0));
        Assert.Equal("jbib_004_u", ClothingNames.Drawable(PedComponent.Jbib, 4));
        Assert.Equal("lowr_123_u", ClothingNames.Drawable(PedComponent.Lowr, 123));
    }

    [Fact]
    public void Diffuse_names_follow_the_observed_game_convention()
    {
        // Verified against real entries inside streamedpeds_mp.rpf.
        Assert.Equal("jbib_diff_000_a_uni", ClothingNames.DiffuseTexture(PedComponent.Jbib, 0));
        Assert.Equal("jbib_diff_000_b_uni", ClothingNames.DiffuseTexture(PedComponent.Jbib, 0, 'b'));
        Assert.Equal("jbib_normal_000", ClothingNames.NormalTexture(PedComponent.Jbib, 0));
        Assert.Equal("jbib_spec_000", ClothingNames.SpecularTexture(PedComponent.Jbib, 0));
    }

    [Fact]
    public void Stream_file_names_carry_the_dlc_prefix_and_caret()
    {
        Assert.Equal(
            "mp_m_freemode_01_fcd_m_jbib_jbib_004^jbib_000_u.ydd",
            ClothingNames.StreamFile("mp_m_freemode_01", "fcd_m_jbib_jbib_004", "jbib_000_u", "ydd"));

        Assert.Equal(
            "mp_m_freemode_01_fcd_m_jbib_jbib_004.ymt",
            ClothingNames.MetadataFile("mp_m_freemode_01", "fcd_m_jbib_jbib_004"));
    }

    [Fact]
    public void Variant_letters_run_a_to_z_and_then_refuse()
    {
        Assert.Equal('a', ClothingNames.VariantLetter(0));
        Assert.Equal('z', ClothingNames.VariantLetter(25));
        Assert.Throws<ArgumentOutOfRangeException>(() => ClothingNames.VariantLetter(26));
    }

    [Fact]
    public void A_diffuse_texture_name_round_trips_through_its_parser()
    {
        var name = ClothingNames.DiffuseTexture(PedComponent.Jbib, 3, 'q', "whi");
        Assert.Equal("jbib_diff_003_q_whi", name);

        Assert.True(ClothingNames.TryParseDiffuseTexture(
            name, out var component, out var drawable, out var variant, out var race));
        Assert.Equal(PedComponent.Jbib, component);
        Assert.Equal(3, drawable);
        Assert.Equal('q', variant);
        Assert.Equal("whi", race);
    }

    [Theory]
    [InlineData("jbib_000_u")]                  // a drawable, not a texture
    [InlineData("jbib_normal_000")]             // four parts, and not "diff"
    [InlineData("jbib_spec_000_a_uni")]         // the right shape, the wrong map
    [InlineData("nope_diff_000_a_uni")]         // no such component prefix
    [InlineData("jbib_diff_00_a_uni")]          // index is not three digits
    [InlineData("jbib_diff_000_ab_uni")]        // slot is one letter, always
    [InlineData("jbib_diff_000_1_uni")]         // ...and a letter, not a digit
    [InlineData("jbib_diff_000_a_")]            // no race
    public void Names_that_are_not_diffuse_textures_are_refused(string name)
    {
        // Refused rather than half-parsed: a caller that gets `true` back goes
        // on to trust the component and drawable it was handed.
        Assert.False(ClothingNames.TryParseDiffuseTexture(name, out _, out _, out _, out _));
    }

    [Fact]
    public void Joaat_is_case_insensitive_and_stable()
    {
        Assert.Equal(Joaat.Hash("jbib_000_u"), Joaat.Hash("JBIB_000_U"));
        Assert.NotEqual(Joaat.Hash("jbib_000_u"), Joaat.Hash("jbib_001_u"));
        Assert.Equal(0u, Joaat.Hash(""));
    }
}

public sealed class ValidationTests
{
    private readonly ValidationEngine _engine = new();

    private static TextureInfo Tex(string name, int w, int h, int mips = 8) =>
        new(name, w, h, "BC3", mips, 20, 0, w * h, "sha");

    [Fact]
    public void Texture_with_a_mismatched_name_is_an_error()
    {
        var report = _engine.ValidateTextureForExport(
            Tex("wrong_name", 512, 512), "jbib_diff_000_a_uni");

        Assert.Equal("RED", report.Status);
        Assert.Contains(report.Findings, f => f.Code == "texture.name");
    }

    [Fact]
    public void Non_power_of_two_texture_is_an_error()
    {
        var report = _engine.ValidateTextureForExport(
            Tex("jbib_diff_000_a_uni", 500, 500), "jbib_diff_000_a_uni");

        Assert.True(report.HasErrors);
        Assert.Contains(report.Findings, f => f.Code == "texture.npot");
    }

    [Fact]
    public void Missing_mip_chain_is_a_warning_not_a_blocker()
    {
        var report = _engine.ValidateTextureForExport(
            Tex("jbib_diff_000_a_uni", 512, 512, mips: 1), "jbib_diff_000_a_uni");

        Assert.False(report.HasErrors);
        Assert.Equal("YELLOW", report.Status);
        Assert.Contains(report.Findings, f => f.Code == "texture.mips");
    }

    [Fact]
    public void Correct_texture_passes_clean()
    {
        var report = _engine.ValidateTextureForExport(
            Tex("jbib_diff_000_a_uni", 512, 512), "jbib_diff_000_a_uni");

        Assert.Equal("GREEN", report.Status);
        Assert.Empty(report.Findings);
    }

    [Fact]
    public void Addon_metadata_must_declare_the_component_it_ships()
    {
        var avail = Enumerable.Repeat(255, 12).ToList();   // nothing available
        var ymt = new PedMetadataInfo(
            "PED_VARIATION", "RSC", 2, "CPedVariationInfo", 0, avail,
            new Dictionary<string, bool>(),
            new[] { new PedComponentInfo(0, 1, 1, new[] { new PedDrawableVariationInfo(1, false) }) },
            1, 0);

        var report = _engine.ValidateAddonMetadata(ymt, PedComponent.Jbib, 1);

        Assert.True(report.HasErrors);
        Assert.Contains(report.Findings, f => f.Code == "ymt.componentunavailable");
    }

    [Fact]
    public void Addon_metadata_texture_count_must_match_what_is_exported()
    {
        var avail = Enumerable.Repeat(255, 12).ToList();
        avail[(int)PedComponent.Jbib] = 0;
        var ymt = new PedMetadataInfo(
            "PED_VARIATION", "RSC", 2, "CPedVariationInfo", 0, avail,
            new Dictionary<string, bool>(),
            new[] { new PedComponentInfo(0, 2, 1, new[] { new PedDrawableVariationInfo(2, false) }) },
            1, 0);

        // Two declared in metadata, three actually exported.
        var report = _engine.ValidateAddonMetadata(ymt, PedComponent.Jbib, 3);

        Assert.True(report.HasErrors);
        Assert.Contains(report.Findings, f => f.Code == "ymt.numavailtex");
    }
}

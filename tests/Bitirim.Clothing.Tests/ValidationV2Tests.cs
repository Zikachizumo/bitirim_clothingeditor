using Bitirim.Clothing.Core.Naming;
using Bitirim.Clothing.Core.Rage;
using Bitirim.Clothing.Editor.Projects;
using Bitirim.Clothing.Editor.Validation;
using Bitirim.Clothing.Validation;
using Xunit;

namespace Bitirim.Clothing.Tests;

/// <summary>
/// Whole-project validation.
/// </summary>
/// <remarks>
/// These run without a window, a WebView or a Python process: the validator
/// takes the document plus whatever was read from the files, so it can be
/// driven with hand-built evidence.
/// </remarks>
public sealed class ValidationV2Tests : IDisposable
{
    private readonly ProjectValidator _validator = new(new ValidationEngine());

    /// <summary>
    /// A real file on disk, because the validator genuinely checks for one.
    /// </summary>
    private readonly string _presentFile =
        Path.Combine(Path.GetTempPath(), $"bcc_valtest_{Guid.NewGuid():N}.bin");

    public ValidationV2Tests() => File.WriteAllBytes(_presentFile, new byte[] { 1 });

    public void Dispose()
    {
        try { File.Delete(_presentFile); } catch { /* best effort */ }
    }

    private static BackendCapabilities Capable(bool writeYdd = false) => new(
        ContractVersion: "1.0.0",
        Backend: "test",
        BackendVersion: "0",
        Python: "3.12",
        Operations: Array.Empty<string>(),
        ReadYdd: true, ReadYtd: true, ReadYmt: true,
        WriteYtd: true, WriteYmt: true,
        WriteYdd: writeYdd, WriteYddGeometry: false,
        EncodeTexture: false);

    private static ClothingProject Project(Action<ClothingProject>? configure = null)
    {
        var document = new ClothingProject
        {
            Name = "Test",
            YmtTemplatePath = "assets/mp_m_freemode_01.ymt",
        };
        document.Assets.Clear();
        document.Assets.Add(new ClothingAsset
        {
            Id = "a1",
            Name = "Jacket",
            Component = PedComponent.Jbib,
            DrawableIndex = 0,
            BaseAssetOrigin = BaseAssetOrigin.Imported,
            BaseYddPath = "assets/jbib_000_u.ydd",
            BaseYtdPath = "assets/jbib_diff_000_a_uni.ytd",
        });
        document.ActiveAssetId = "a1";
        document.Variations[0].TexturePath = "textures/var0.png";
        document.Export.ResourceName = "bcc_test";
        document.Export.DlcName = "bcc_m_jbib_test";

        configure?.Invoke(document);
        return document;
    }

    /// <summary>Evidence saying the files are all present and readable.</summary>
    private List<AssetEvidence> Healthy(ClothingProject document) =>
        document.Assets
            .Select(a => new AssetEvidence(a, _presentFile, _presentFile, null, null, null))
            .ToList();

    private static bool Has(ValidationReport report, string code) =>
        report.Findings.Any(f => f.Code == code);

    // ------------------------------------------------------------------

    [Fact]
    public void VALIDATION_TEST_a_healthy_project_produces_no_errors()
    {
        var document = Project();

        var report = _validator.Validate(document, Healthy(document), Capable());

        var errors = report.Findings.Where(f => f.Severity == Severity.Error).ToList();
        Assert.True(errors.Count == 0,
            "unexpected errors: " + string.Join("; ", errors.Select(e => $"{e.Code}: {e.Message}")));
    }

    [Fact]
    public void VALIDATION_TEST_a_mock_garment_blocks_export()
    {
        var document = Project(d => d.Assets[0].BaseAssetOrigin = BaseAssetOrigin.Mock);

        var report = _validator.Validate(document, Healthy(document), Capable());

        Assert.True(Has(report, "project.mock"));
        Assert.True(report.HasErrors);
    }

    [Fact]
    public void VALIDATION_TEST_a_missing_drawable_is_an_error()
    {
        var document = Project(d => d.Assets[0].BaseYddPath = null);

        var report = _validator.Validate(document, Healthy(document), Capable());

        Assert.True(Has(report, "project.noydd"));
    }

    [Fact]
    public void VALIDATION_TEST_a_drawable_that_vanished_from_the_folder_is_an_error()
    {
        var document = Project();
        var evidence = new List<AssetEvidence>
        {
            new(document.Assets[0],
                Path.Combine(Path.GetTempPath(), "definitely-not-here.ydd"),
                _presentFile, null, null, null),
        };

        var report = _validator.Validate(document, evidence, Capable());

        Assert.True(Has(report, "asset.missingydd"));
    }

    [Fact]
    public void VALIDATION_TEST_no_ymt_template_is_an_error()
    {
        var document = Project(d => d.YmtTemplatePath = null);

        var report = _validator.Validate(document, Healthy(document), Capable());

        Assert.True(Has(report, "ymt.notemplate"));
    }

    [Fact]
    public void VALIDATION_TEST_two_garments_in_the_same_slot_clash()
    {
        var document = Project(d => d.Assets.Add(new ClothingAsset
        {
            Id = "a2",
            Name = "Second jacket",
            Component = PedComponent.Jbib,
            DrawableIndex = 0,
            BaseAssetOrigin = BaseAssetOrigin.Imported,
            BaseYddPath = "assets/other.ydd",
            BaseYtdPath = "assets/other.ytd",
        }));

        var report = _validator.Validate(document, Healthy(document), Capable());

        Assert.True(Has(report, "project.slotclash"));
        Assert.True(report.HasErrors);
    }

    [Fact]
    public void VALIDATION_TEST_different_slots_do_not_clash()
    {
        var document = Project(d => d.Assets.Add(new ClothingAsset
        {
            Id = "a2",
            Name = "Trousers",
            Component = PedComponent.Lowr,
            DrawableIndex = 0,
            BaseAssetOrigin = BaseAssetOrigin.Imported,
            BaseYddPath = "assets/lowr.ydd",
            BaseYtdPath = "assets/lowr.ytd",
        }));

        var report = _validator.Validate(document, Healthy(document), Capable());

        Assert.False(Has(report, "project.slotclash"));
    }

    [Theory]
    [InlineData("has spaces")]
    [InlineData("UPPER-and-dots.here")]
    [InlineData("9starts-with-a-digit")]
    [InlineData("")]
    public void VALIDATION_TEST_an_unusable_resource_name_is_an_error(string name)
    {
        var document = Project(d => d.Export.ResourceName = name);

        var report = _validator.Validate(document, Healthy(document), Capable());

        Assert.True(Has(report, "export.resourcename") || Has(report, "export.noresourcename"));
    }

    [Theory]
    [InlineData("bcc_m_jbib_test")]
    [InlineData("my-pack-01")]
    public void VALIDATION_TEST_a_usable_resource_name_passes(string name)
    {
        var document = Project(d => d.Export.ResourceName = name);

        var report = _validator.Validate(document, Healthy(document), Capable());

        Assert.False(Has(report, "export.resourcename"));
    }

    [Fact]
    public void VALIDATION_TEST_a_very_long_dlc_name_is_warned_about()
    {
        var document = Project(d =>
            d.Export.DlcName = new string('x', 64));

        var report = _validator.Validate(document, Healthy(document), Capable());

        Assert.True(Has(report, "export.longname"));
    }

    [Fact]
    public void VALIDATION_TEST_an_unsupported_texture_format_is_an_error()
    {
        var document = Project(d => d.Export.TextureFormat = "PNG");

        var report = _validator.Validate(document, Healthy(document), Capable());

        Assert.True(Has(report, "export.format"));
    }

    [Fact]
    public void VALIDATION_TEST_an_unsaved_variation_is_a_warning_not_an_error()
    {
        var document = Project(d => d.Variations[0].TexturePath = null);

        var report = _validator.Validate(document, Healthy(document), Capable());

        var finding = Assert.Single(report.Findings, f => f.Code == "texture.unsaved");
        Assert.Equal(Severity.Warning, finding.Severity);
    }

    [Fact]
    public void VALIDATION_TEST_excluding_every_garment_is_an_error()
    {
        var document = Project(d => d.Assets[0].IncludeInExport = false);

        var report = _validator.Validate(document, Healthy(document), Capable());

        Assert.True(Has(report, "export.nothing"));
    }

    [Fact]
    public void VALIDATION_TEST_a_missing_backend_is_reported_as_such()
    {
        var document = Project();

        var report = _validator.Validate(document, Healthy(document), capabilities: null);

        Assert.True(Has(report, "validate.nobackend"));
    }

    [Fact]
    public void VALIDATION_TEST_the_disabled_ydd_writer_is_information_not_a_defect()
    {
        var document = Project();

        var report = _validator.Validate(document, Healthy(document), Capable(writeYdd: false));

        var finding = Assert.Single(report.Findings, f => f.Code == "asset.yddreadonly");
        Assert.Equal(Severity.Info, finding.Severity);
    }

    [Fact]
    public void VALIDATION_TEST_every_report_says_the_export_is_unverified_in_game()
    {
        var document = Project();

        var report = _validator.Validate(document, Healthy(document), Capable());

        var finding = Assert.Single(report.Findings, f => f.Code == "export.unverified");
        Assert.Contains("FiveM", finding.Message);
    }

    [Fact]
    public void VALIDATION_TEST_findings_carry_a_category_for_the_panel()
    {
        var document = Project(d => d.Export.TextureFormat = "PNG");

        var report = _validator.Validate(document, Healthy(document), Capable());

        Assert.All(report.Findings, f => Assert.False(string.IsNullOrWhiteSpace(f.Category)));
        Assert.Equal(FindingCategories.Export,
            report.Findings.First(f => f.Code == "export.format").Category);
    }

    [Fact]
    public void VALIDATION_TEST_a_finding_can_point_at_what_it_is_about()
    {
        var document = Project(d => d.Assets[0].BaseAssetOrigin = BaseAssetOrigin.Mock);

        var report = _validator.Validate(document, Healthy(document), Capable());

        Assert.Equal("asset:a1", report.Findings.First(f => f.Code == "project.mock").Target);
    }

    [Fact]
    public void VALIDATION_TEST_a_backend_read_failure_downgrades_to_a_warning()
    {
        var document = Project();
        var evidence = new List<AssetEvidence>
        {
            new(document.Assets[0], _presentFile, _presentFile,
                null, null, "the drawable could not be parsed"),
        };

        var report = _validator.Validate(document, evidence, Capable());

        var finding = Assert.Single(report.Findings, f => f.Code == "validate.backend");
        Assert.Equal(Severity.Warning, finding.Severity);
    }

    [Fact]
    public void CATEGORY_TEST_codes_map_to_the_panel_group_a_person_would_expect()
    {
        Assert.Equal(FindingCategories.Texture, FindingCategories.For("texture.mips"));
        Assert.Equal(FindingCategories.Asset, FindingCategories.For("ydd.lod"));
        Assert.Equal(FindingCategories.Metadata, FindingCategories.For("ymt.availcomp"));
        Assert.Equal(FindingCategories.Uv, FindingCategories.For("uv.missing"));
        Assert.Equal(FindingCategories.Material, FindingCategories.For("material.shader"));
        Assert.Equal(FindingCategories.Export, FindingCategories.For("export.readback"));
        Assert.Equal(FindingCategories.Mesh, FindingCategories.For("mesh.nonormals"));
        Assert.Equal(FindingCategories.Project, FindingCategories.For("something.unmapped"));
    }

    [Fact]
    public void CATEGORY_TEST_an_explicit_category_beats_the_code_prefix()
    {
        var finding = new Finding(Severity.Info, "texture.thing", "message")
        {
            Category = FindingCategories.Export,
        };

        Assert.Equal(FindingCategories.Export, finding.Category);
    }
}

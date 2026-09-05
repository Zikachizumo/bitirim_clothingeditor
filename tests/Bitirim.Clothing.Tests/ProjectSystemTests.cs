using System.IO.Compression;
using Bitirim.Clothing.Core.Naming;
using Bitirim.Clothing.Editor.Infrastructure;
using Bitirim.Clothing.Editor.Projects;
using Xunit;

namespace Bitirim.Clothing.Tests;

/// <summary>
/// Project create / save / load / package round-trips.
/// </summary>
/// <remarks>
/// No fixtures and no asset backend needed: these exercise the project system
/// itself, which is the part a user loses work through if it is wrong.
/// </remarks>
public sealed class ProjectSystemTests : IDisposable
{
    private readonly string _root;
    private readonly ProjectService _service;

    public ProjectSystemTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bcc_projtest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
        _service = new ProjectService(new LogService());
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private ClothingProject Sample(string name = "Test Jacket") => new()
    {
        Name = name,
        Author = "tester",
        Male = true,
        Component = PedComponent.Jbib,
        DrawableIndex = 4,
        BaseAssetOrigin = BaseAssetOrigin.Mock,
    };

    [Fact]
    public void PROJECT_CREATE_TEST_lays_out_the_expected_folders()
    {
        var project = _service.Create(_root, Sample());

        Assert.True(File.Exists(project.ProjectJsonPath));
        foreach (var sub in new[] { "assets", "textures", "layers", "previews", "exports", "backups" })
            Assert.True(Directory.Exists(Path.Combine(project.Directory, sub)), $"missing {sub}/");
    }

    [Fact]
    public void PROJECT_CREATE_TEST_does_not_overwrite_an_existing_project()
    {
        var first = _service.Create(_root, Sample());
        var second = _service.Create(_root, Sample());

        Assert.NotEqual(first.Directory, second.Directory);
        Assert.True(File.Exists(first.ProjectJsonPath));
        Assert.True(File.Exists(second.ProjectJsonPath));
    }

    [Fact]
    public void PROJECT_SAVE_LOAD_TEST_round_trips_every_field()
    {
        var document = Sample("Round Trip");
        document.Variations.Add(new TextureVariation { Name = "Red", Index = 1 });
        document.Variations[0].Layers.Add(new TextureLayer
        {
            Name = "logo.png", Kind = LayerKind.Image,
            X = 12, Y = 34, Width = 100, Height = 80, Opacity = 0.5,
            Source = "layers/logo.png",
        });
        document.Export.ResourceName = "my_resource";
        document.Export.DlcName = "my_dlc";

        var created = _service.Create(_root, document);
        _service.Save(created);

        var reopened = _service.Open(created.Directory);
        var d = reopened.Document;

        Assert.Equal("Round Trip", d.Name);
        Assert.Equal("tester", d.Author);
        Assert.True(d.Male);
        Assert.Equal(PedComponent.Jbib, d.Component);
        Assert.Equal(4, d.DrawableIndex);
        Assert.Equal(BaseAssetOrigin.Mock, d.BaseAssetOrigin);
        Assert.Equal(2, d.Variations.Count);
        Assert.Equal("Red", d.Variations[1].Name);

        var layer = d.Variations[0].Layers.Single();
        Assert.Equal(LayerKind.Image, layer.Kind);
        Assert.Equal(0.5, layer.Opacity);
        Assert.Equal("layers/logo.png", layer.Source);
        Assert.Equal(100, layer.Width);

        Assert.Equal("my_resource", d.Export.ResourceName);
        Assert.Equal("my_dlc", d.Export.DlcName);
    }

    [Fact]
    public void PROJECT_PACKAGE_TEST_round_trips_through_a_bitirimclothing_file()
    {
        var created = _service.Create(_root, Sample("Packaged"));
        File.WriteAllText(Path.Combine(created.Directory, "assets", "note.txt"), "carried along");

        var packagePath = Path.Combine(_root, "Packaged" + ProjectService.PackageExtension);
        var packaged = _service.SaveAsPackage(created, packagePath);

        Assert.True(File.Exists(packagePath));
        Assert.Equal(packagePath, packaged.PackagePath);

        var reopened = _service.Open(packagePath);
        Assert.Equal("Packaged", reopened.Document.Name);
        Assert.Equal(packagePath, reopened.PackagePath);
        Assert.Equal("carried along",
            File.ReadAllText(Path.Combine(reopened.Directory, "assets", "note.txt")));
    }

    [Fact]
    public void PROJECT_PACKAGE_TEST_excludes_built_exports()
    {
        var created = _service.Create(_root, Sample("NoExports"));
        Directory.CreateDirectory(Path.Combine(created.Directory, "exports", "some_resource"));
        File.WriteAllText(
            Path.Combine(created.Directory, "exports", "some_resource", "fxmanifest.lua"), "x");

        var packagePath = Path.Combine(_root, "NoExports" + ProjectService.PackageExtension);
        _service.SaveAsPackage(created, packagePath);

        using var zip = ZipFile.OpenRead(packagePath);
        Assert.DoesNotContain(zip.Entries,
            e => e.FullName.StartsWith("exports/", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(zip.Entries, e => e.FullName == "project.json");
    }

    [Fact]
    public void ASSET_IMPORT_TEST_copies_the_file_into_the_project()
    {
        var created = _service.Create(_root, Sample());
        var external = Path.Combine(_root, "outside.png");
        File.WriteAllBytes(external, new byte[] { 1, 2, 3, 4 });

        var relative = ProjectService.ImportInto(created, external, "layers");

        Assert.Equal("layers/outside.png", relative);
        var full = ProjectService.ResolveInProject(created, relative);
        Assert.True(File.Exists(full));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(full));
    }

    [Fact]
    public void ASSET_IMPORT_TEST_does_not_clobber_a_different_file_of_the_same_name()
    {
        var created = _service.Create(_root, Sample());

        var a = Path.Combine(_root, "a", "logo.png");
        var b = Path.Combine(_root, "b", "logo.png");
        Directory.CreateDirectory(Path.GetDirectoryName(a)!);
        Directory.CreateDirectory(Path.GetDirectoryName(b)!);
        File.WriteAllBytes(a, new byte[] { 1 });
        File.WriteAllBytes(b, new byte[] { 2 });

        var first = ProjectService.ImportInto(created, a, "layers");
        var second = ProjectService.ImportInto(created, b, "layers");

        Assert.NotEqual(first, second);
        Assert.Equal(new byte[] { 1 },
            File.ReadAllBytes(ProjectService.ResolveInProject(created, first)));
        Assert.Equal(new byte[] { 2 },
            File.ReadAllBytes(ProjectService.ResolveInProject(created, second)));
    }

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("..\\..\\windows\\system32\\drivers\\etc\\hosts")]
    [InlineData("assets/../../outside.png")]
    public void A_project_path_that_escapes_the_root_is_refused(string relative)
    {
        var created = _service.Create(_root, Sample());

        var ex = Assert.Throws<EditorException>(
            () => ProjectService.ResolveInProject(created, relative));
        Assert.Equal("path_escape", ex.Code);
    }

    [Fact]
    public void A_project_from_a_newer_schema_is_refused_with_a_readable_message()
    {
        var created = _service.Create(_root, Sample());
        var json = System.Text.RegularExpressions.Regex.Replace(
            File.ReadAllText(created.ProjectJsonPath),
            "\"schemaVersion\": *\\d+",
            "\"schemaVersion\": 99");
        File.WriteAllText(created.ProjectJsonPath, json);

        var ex = Assert.Throws<EditorException>(() => _service.Open(created.Directory));
        Assert.Equal("project_newer", ex.Code);
        Assert.Contains("newer version", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_corrupt_project_reports_a_readable_message_not_a_parser_error()
    {
        var created = _service.Create(_root, Sample());
        File.WriteAllText(created.ProjectJsonPath, "{ this is not json");

        var ex = Assert.Throws<EditorException>(() => _service.Open(created.Directory));
        Assert.Equal("project_corrupt", ex.Code);
        Assert.DoesNotContain("Exception", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Opening_something_that_is_not_a_project_says_so_plainly()
    {
        var stray = Path.Combine(_root, "holiday.jpg");
        File.WriteAllText(stray, "not a project");

        var ex = Assert.Throws<EditorException>(() => _service.Open(stray));
        Assert.Equal("unsupported_project", ex.Code);
    }

    [Fact]
    public void PROJECT_BACKUP_TEST_writes_a_timestamped_snapshot()
    {
        var created = _service.Create(_root, Sample());
        var backup = _service.Backup(created);

        Assert.True(File.Exists(backup));
        Assert.StartsWith(Path.Combine(created.Directory, "backups"), backup);
        Assert.Equal(File.ReadAllText(created.ProjectJsonPath), File.ReadAllText(backup));
    }
}

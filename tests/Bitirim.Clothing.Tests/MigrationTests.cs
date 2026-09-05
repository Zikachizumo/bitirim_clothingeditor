using System.Text.Json;
using Bitirim.Clothing.Core.Naming;
using Bitirim.Clothing.Editor.Infrastructure;
using Bitirim.Clothing.Editor.Projects;
using Xunit;

namespace Bitirim.Clothing.Tests;

/// <summary>
/// Schema 1 to schema 2 migration.
/// </summary>
/// <remarks>
/// A v0.1.0 project must open in v0.2.0 without the user doing anything, and
/// without losing a layer. These tests are written against real schema-1 JSON
/// -- the exact shape v0.1.0 wrote -- rather than against the current model, so
/// they keep testing the migration even as the model moves on.
/// </remarks>
public sealed class MigrationTests : IDisposable
{
    private readonly string _root;
    private readonly ProjectService _service;

    public MigrationTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bcc_migtest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
        _service = new ProjectService(new LogService());
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    /// <summary>A project.json exactly as v0.1.0 wrote it.</summary>
    private const string Schema1 = """
    {
      "schemaVersion": 1,
      "appVersion": "0.1.0",
      "name": "Real Test Jacket",
      "author": "Bitirim",
      "createdAt": "2026-08-31T12:00:00+00:00",
      "updatedAt": "2026-08-31T12:34:00+00:00",
      "male": true,
      "component": "Jbib",
      "drawableIndex": 4,
      "baseAssetOrigin": "Imported",
      "baseYddPath": "assets/jbib_000_u.ydd",
      "baseYtdPath": "assets/jbib_diff_000_a_uni.ytd",
      "ymtTemplatePath": "assets/mp_m_freemode_01.ymt",
      "variations": [
        {
          "id": "var0",
          "name": "Original",
          "index": 0,
          "texturePath": "textures/var0.png",
          "layers": [
            {
              "id": "layerA",
              "name": "logo.png",
              "kind": "Image",
              "visible": true,
              "opacity": 0.75,
              "x": 128, "y": 96, "width": 171, "height": 168, "rotation": 12,
              "source": "layers/logo.png"
            },
            {
              "id": "layerB",
              "name": "Brush 1",
              "kind": "Brush",
              "visible": true,
              "opacity": 1,
              "x": 0, "y": 0, "width": 512, "height": 512, "rotation": 0,
              "color": "#c81e1e"
            }
          ]
        },
        {
          "id": "var1",
          "name": "Red",
          "index": 1,
          "layers": []
        }
      ],
      "export": {
        "resourceName": "bcc_ui_test_jbib",
        "dlcName": "bcc_m_jbib_uitest",
        "textureFormat": "BC3",
        "manifestMode": "Stream"
      }
    }
    """;

    private string WriteSchema1Project(string json = Schema1)
    {
        var dir = Path.Combine(_root, "LegacyProject_" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "project.json"), json);
        return dir;
    }

    [Fact]
    public void PROJECT_MIGRATION_TEST_opens_a_schema_1_project()
    {
        var opened = _service.Open(WriteSchema1Project());

        Assert.Equal(1, opened.OpenedFromSchema);
        Assert.Equal(ClothingProject.CurrentSchemaVersion, opened.Document.SchemaVersion);
        Assert.NotEmpty(opened.MigrationNotes);
    }

    [Fact]
    public void PROJECT_MIGRATION_TEST_folds_the_single_garment_into_the_asset_list()
    {
        var document = _service.Open(WriteSchema1Project()).Document;

        var asset = Assert.Single(document.Assets);
        Assert.Equal("Real Test Jacket", asset.Name);
        Assert.Equal(PedComponent.Jbib, asset.Component);
        Assert.Equal(4, asset.DrawableIndex);
        Assert.Equal(BaseAssetOrigin.Imported, asset.BaseAssetOrigin);
        Assert.Equal("assets/jbib_000_u.ydd", asset.BaseYddPath);
        Assert.Equal("assets/jbib_diff_000_a_uni.ytd", asset.BaseYtdPath);
        Assert.Equal(asset.Id, document.ActiveAssetId);
    }

    [Fact]
    public void PROJECT_MIGRATION_TEST_keeps_project_level_fields()
    {
        var document = _service.Open(WriteSchema1Project()).Document;

        Assert.Equal("Real Test Jacket", document.Name);
        Assert.Equal("Bitirim", document.Author);
        Assert.True(document.Male);
        Assert.Equal("assets/mp_m_freemode_01.ymt", document.YmtTemplatePath);
        Assert.Equal("bcc_ui_test_jbib", document.Export.ResourceName);
        Assert.Equal("bcc_m_jbib_uitest", document.Export.DlcName);
        Assert.Equal("BC3", document.Export.TextureFormat);
    }

    [Fact]
    public void PROJECT_MIGRATION_TEST_loses_no_layer()
    {
        var document = _service.Open(WriteSchema1Project()).Document;

        Assert.Equal(2, document.Variations.Count);
        Assert.Equal("Original", document.Variations[0].Name);
        Assert.Equal("textures/var0.png", document.Variations[0].TexturePath);
        Assert.Equal(2, document.Variations[0].Layers.Count);

        var image = document.Variations[0].Layers[0];
        Assert.Equal(LayerKind.Image, image.Kind);
        Assert.Equal(0.75, image.Opacity);
        Assert.Equal(171, image.Width);
        Assert.Equal(12, image.Rotation);
        Assert.Equal("layers/logo.png", image.Source);
    }

    [Fact]
    public void PROJECT_MIGRATION_TEST_says_so_when_brush_pixels_cannot_come_across()
    {
        // Schema 1 kept brush pixels only in memory. Migrating must not pretend
        // the strokes survived: it has to say what was lost.
        var notes = _service.Open(WriteSchema1Project()).MigrationNotes;

        Assert.Contains(notes, n => n.Contains("brush layer", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PROJECT_MIGRATION_TEST_keeps_the_original_file_under_backups()
    {
        var directory = WriteSchema1Project();
        var original = File.ReadAllText(Path.Combine(directory, "project.json"));

        _service.Open(directory);

        var preserved = Path.Combine(directory, "backups", "schema1", "project.json");
        Assert.True(File.Exists(preserved), "the pre-migration file must be kept");
        Assert.Equal(original, File.ReadAllText(preserved));
    }

    [Fact]
    public void PROJECT_MIGRATION_TEST_survives_a_save_and_reopen()
    {
        var directory = WriteSchema1Project();

        var migrated = _service.Open(directory);
        _service.Save(migrated);

        var reopened = _service.Open(directory);
        Assert.Equal(ClothingProject.CurrentSchemaVersion, reopened.OpenedFromSchema);
        Assert.Single(reopened.Document.Assets);
        Assert.Equal(2, reopened.Document.Variations[0].Layers.Count);
    }

    [Fact]
    public void PROJECT_MIGRATION_TEST_repairs_a_document_with_no_assets()
    {
        var directory = WriteSchema1Project("""
        { "schemaVersion": 2, "name": "Empty", "assets": [] }
        """);

        var opened = _service.Open(directory);

        Assert.Single(opened.Document.Assets);
        Assert.Single(opened.Document.Assets[0].Variations);
        Assert.Contains(opened.Document.ActiveAssetId, new[] { opened.Document.Assets[0].Id });
    }

    [Fact]
    public void PROJECT_MIGRATION_TEST_reindexes_variations_into_a_dense_run()
    {
        // Variation indices become GTA variant letters, so gaps would produce
        // file names that do not line up with the metadata.
        var directory = WriteSchema1Project("""
        {
          "schemaVersion": 2,
          "name": "Gappy",
          "assets": [{
            "id": "a1", "name": "G", "component": "Jbib", "drawableIndex": 0,
            "variations": [
              { "id": "v0", "name": "A", "index": 0, "layers": [] },
              { "id": "v9", "name": "B", "index": 9, "layers": [] }
            ]
          }],
          "activeAssetId": "a1"
        }
        """);

        var variations = _service.Open(directory).Document.Variations;

        Assert.Equal(new[] { 0, 1 }, variations.Select(v => v.Index));
        Assert.Equal('b', variations[1].Variant);
    }

    [Fact]
    public void PROJECT_MIGRATION_TEST_drops_a_parent_id_that_points_at_nothing()
    {
        var directory = WriteSchema1Project("""
        {
          "schemaVersion": 2,
          "name": "Orphan",
          "assets": [{
            "id": "a1", "name": "G", "component": "Jbib", "drawableIndex": 0,
            "variations": [{
              "id": "v0", "name": "A", "index": 0,
              "layers": [
                { "id": "l1", "name": "Stray", "kind": "Fill", "visible": true,
                  "opacity": 1, "x": 0, "y": 0, "width": 0, "height": 0, "rotation": 0,
                  "parentId": "does-not-exist" }
              ]
            }]
          }],
          "activeAssetId": "a1"
        }
        """);

        var layer = _service.Open(directory).Document.Variations[0].Layers[0];
        Assert.Null(layer.ParentId);
    }

    [Fact]
    public void PROJECT_MIGRATION_TEST_clamps_an_out_of_range_opacity()
    {
        var directory = WriteSchema1Project("""
        {
          "schemaVersion": 2,
          "name": "Weird",
          "assets": [{
            "id": "a1", "name": "G", "component": "Jbib", "drawableIndex": 0,
            "variations": [{
              "id": "v0", "name": "A", "index": 0,
              "layers": [
                { "id": "l1", "name": "X", "kind": "Fill", "visible": true,
                  "opacity": 4.5, "x": 0, "y": 0, "width": 0, "height": 0, "rotation": 0 }
              ]
            }]
          }],
          "activeAssetId": "a1"
        }
        """);

        Assert.Equal(1.0, _service.Open(directory).Document.Variations[0].Layers[0].Opacity);
    }

    [Fact]
    public void PROJECT_MIGRATION_TEST_refuses_a_newer_schema_rather_than_guessing()
    {
        var directory = WriteSchema1Project("""
        { "schemaVersion": 99, "name": "From the future" }
        """);

        var ex = Assert.Throws<EditorException>(() => _service.Open(directory));
        Assert.Equal("project_newer", ex.Code);
    }

    [Fact]
    public void The_active_asset_projection_reads_and_writes_the_selected_garment()
    {
        var document = new ClothingProject { Name = "Two" };
        document.Assets.Add(new ClothingAsset
        {
            Id = "first", Name = "Jacket", Component = PedComponent.Jbib, DrawableIndex = 3,
        });
        document.Assets.Add(new ClothingAsset
        {
            Id = "second", Name = "Trousers", Component = PedComponent.Lowr, DrawableIndex = 7,
        });

        document.ActiveAssetId = "second";
        Assert.Equal(PedComponent.Lowr, document.Component);
        Assert.Equal(7, document.DrawableIndex);

        document.DrawableIndex = 9;
        Assert.Equal(9, document.Assets[1].DrawableIndex);
        Assert.Equal(3, document.Assets[0].DrawableIndex);
    }

    [Fact]
    public void The_active_asset_projection_never_throws_on_an_empty_document()
    {
        var document = new ClothingProject();

        // Reading Active must not require anyone to have created a garment first.
        Assert.NotNull(document.Active);
        Assert.Single(document.Assets);
        Assert.False(document.IsMock);
    }

    [Fact]
    public void MULTI_ASSET_TEST_only_exportable_garments_are_offered()
    {
        var document = new ClothingProject();
        document.Assets.Clear();
        document.Assets.Add(new ClothingAsset
        {
            Id = "real", Name = "Real", BaseYddPath = "assets/a.ydd",
            BaseAssetOrigin = BaseAssetOrigin.Imported,
        });
        document.Assets.Add(new ClothingAsset
        {
            Id = "mock", Name = "Mock", BaseAssetOrigin = BaseAssetOrigin.Mock,
        });
        document.Assets.Add(new ClothingAsset
        {
            Id = "excluded", Name = "Excluded", BaseYddPath = "assets/c.ydd",
            BaseAssetOrigin = BaseAssetOrigin.Imported, IncludeInExport = false,
        });

        var exportable = document.ExportableAssets;

        Assert.Equal("real", Assert.Single(exportable).Id);
    }

    [Fact]
    public void PROJECT_MIGRATION_TEST_round_trips_brush_strokes_and_clips()
    {
        var document = new ClothingProject { Name = "Strokes" };
        document.Variations[0].Layers.Add(new TextureLayer
        {
            Name = "Brush 1",
            Kind = LayerKind.Brush,
            BlendMode = "multiply",
            Strokes = new List<BrushStroke>
            {
                new()
                {
                    Color = "#ff0000", Size = 18, Erase = false, Softness = 0.4,
                    Points = new List<double> { 10, 12, 30, 44 },
                    Clip = new StrokeClip
                    {
                        Kind = "ellipse", X = 4, Y = 5, Width = 100, Height = 120,
                    },
                },
            },
        });

        var json = ProjectSerialization.Serialize(document);
        var back = ProjectSerialization.Deserialize(json);

        var layer = back.Variations[0].Layers[0];
        Assert.Equal("multiply", layer.BlendMode);
        var stroke = Assert.Single(layer.Strokes!);
        Assert.Equal(new List<double> { 10, 12, 30, 44 }, stroke.Points);
        Assert.Equal(0.4, stroke.Softness);
        Assert.Equal("ellipse", stroke.Clip!.Kind);
        Assert.Equal(120, stroke.Clip.Height);
    }

    [Fact]
    public void PROJECT_MIGRATION_TEST_a_corrupt_variation_array_costs_layers_not_the_project()
    {
        var directory = WriteSchema1Project("""
        {
          "schemaVersion": 1,
          "name": "Half broken",
          "component": "Uppr",
          "drawableIndex": 2,
          "variations": "this should have been an array"
        }
        """);

        var document = _service.Open(directory).Document;

        Assert.Equal(PedComponent.Uppr, document.Component);
        Assert.Equal(2, document.DrawableIndex);
        Assert.Single(document.Variations);   // a default one was put back
    }

    [Fact]
    public void Serialization_options_are_shared_so_snapshots_match_the_saved_file()
    {
        var created = _service.Create(_root, new ClothingProject { Name = "Snapshot" });

        var onDisk = File.ReadAllText(created.ProjectJsonPath);
        var snapshot = ProjectSerialization.Serialize(created.Document);

        // A formatting difference here would make crash recovery fire after
        // every clean shutdown.
        Assert.Equal(onDisk, snapshot);
    }

    [Fact]
    public void Unknown_fields_in_a_project_are_ignored_rather_than_trusted()
    {
        var directory = WriteSchema1Project("""
        {
          "schemaVersion": 2,
          "name": "Curious",
          "assets": [{ "id": "a1", "name": "G", "component": "Jbib", "drawableIndex": 0,
                       "variations": [{ "id": "v0", "name": "A", "index": 0, "layers": [] }] }],
          "activeAssetId": "a1",
          "runThis": "cmd.exe /c calc",
          "__proto__": { "polluted": true }
        }
        """);

        var document = _service.Open(directory).Document;

        Assert.Equal("Curious", document.Name);
        Assert.Single(document.Assets);
    }

    [Fact]
    public void A_document_is_serialisable_without_any_null_noise()
    {
        var json = ProjectSerialization.Serialize(new ClothingProject { Name = "Clean" });

        using var parsed = JsonDocument.Parse(json);
        var root = parsed.RootElement;

        // Nulls are omitted, so an empty project stays readable by a person.
        Assert.False(root.TryGetProperty("ymtTemplatePath", out _));
        Assert.True(root.TryGetProperty("assets", out var assets));
        Assert.Equal(JsonValueKind.Array, assets.ValueKind);
    }
}

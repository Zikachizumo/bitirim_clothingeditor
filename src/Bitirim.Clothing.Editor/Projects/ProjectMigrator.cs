using System.Text.Json;
using Bitirim.Clothing.Core.Naming;

namespace Bitirim.Clothing.Editor.Projects;

/// <summary>
/// Brings older project documents up to the current schema.
/// </summary>
/// <remarks>
/// Migration runs on the parsed JSON, before the document is handed to the
/// application, and it never writes over the file it read. The caller keeps a
/// copy of the original under <c>backups/</c> first, so a migration that turns
/// out to be wrong costs the user nothing.
///
/// The rule for every step: <em>read what is there, invent nothing</em>. A field
/// that schema 1 did not have gets the current default, not a guess at what the
/// user would have chosen.
/// </remarks>
public static class ProjectMigrator
{
    /// <summary>Result of inspecting a document before it is used.</summary>
    public sealed record MigrationResult(
        ClothingProject Document,
        int FromVersion,
        int ToVersion,
        IReadOnlyList<string> Notes)
    {
        public bool Migrated => FromVersion != ToVersion;
    }

    /// <summary>
    /// Parses and, if needed, upgrades a project document.
    /// </summary>
    /// <param name="json">Raw <c>project.json</c> text.</param>
    /// <param name="options">The same serializer options the service writes with.</param>
    public static MigrationResult Load(string json, JsonSerializerOptions options)
    {
        using var probe = JsonDocument.Parse(json);
        var root = probe.RootElement;

        var version = root.TryGetProperty("schemaVersion", out var v) && v.TryGetInt32(out var parsed)
            ? parsed
            : 1;

        if (version > ClothingProject.CurrentSchemaVersion)
        {
            throw new Infrastructure.EditorException("project_newer",
                $"This project was made with a newer version (schema {version}). "
                + "Update Bitirim Clothing Creator to open it.");
        }

        var document = JsonSerializer.Deserialize<ClothingProject>(json, options)
                       ?? throw new Infrastructure.EditorException(
                           "project_corrupt", "The project file is empty.");

        var notes = new List<string>();

        if (version < 2)
        {
            MigrateOneToTwo(root, document, notes);
        }

        Normalise(document, notes);
        document.SchemaVersion = ClothingProject.CurrentSchemaVersion;

        return new MigrationResult(document, version, ClothingProject.CurrentSchemaVersion, notes);
    }

    /// <summary>
    /// Schema 1 → 2: the single top-level garment becomes <c>assets[0]</c>.
    /// </summary>
    /// <remarks>
    /// The schema-1 fields are <see cref="System.Text.Json.Serialization.JsonIgnoreAttribute"/>
    /// projections on the current model, so they are not populated by
    /// deserialisation. They are read straight off the JSON here instead.
    /// </remarks>
    private static void MigrateOneToTwo(JsonElement root, ClothingProject document, List<string> notes)
    {
        var asset = new ClothingAsset
        {
            Name = document.Name,
            Component = ReadEnum(root, "component", PedComponent.Jbib),
            DrawableIndex = ReadInt(root, "drawableIndex", 0),
            BaseAssetOrigin = ReadEnum(root, "baseAssetOrigin", BaseAssetOrigin.None),
            BaseYddPath = ReadString(root, "baseYddPath"),
            BaseYtdPath = ReadString(root, "baseYtdPath"),
            Variations = ReadVariations(root),
        };

        if (asset.Variations.Count == 0)
            asset.Variations.Add(new TextureVariation { Name = "Original", Index = 0 });

        document.Assets = new List<ClothingAsset> { asset };
        document.ActiveAssetId = asset.Id;

        notes.Add($"Moved the garment '{asset.Name}' into the new multi-asset layout "
                  + $"({asset.Variations.Count} variation(s) preserved).");

        // Schema 1 brush layers stored no stroke data: the pixels only ever
        // existed in memory and were baked into textures/<id>.png on save. Say
        // so rather than silently presenting an empty layer as intact.
        var emptyBrushes = asset.Variations
            .SelectMany(v => v.Layers)
            .Count(l => l.Kind == LayerKind.Brush && (l.Strokes is null || l.Strokes.Count == 0));

        if (emptyBrushes > 0)
        {
            notes.Add($"{emptyBrushes} brush layer(s) from the older format carry no stroke data. "
                      + "Their pixels are still in the saved texture, but they cannot be re-edited.");
        }
    }

    /// <summary>Repairs invariants that any version could have violated.</summary>
    private static void Normalise(ClothingProject document, List<string> notes)
    {
        if (document.Assets.Count == 0)
        {
            document.Assets.Add(new ClothingAsset { Name = document.Name });
            notes.Add("The project had no garment; an empty one was added.");
        }

        if (document.ActiveAssetId is null
            || document.Assets.All(a => a.Id != document.ActiveAssetId))
        {
            document.ActiveAssetId = document.Assets[0].Id;
        }

        foreach (var asset in document.Assets)
        {
            if (asset.Variations.Count == 0)
                asset.Variations.Add(new TextureVariation { Name = "Original", Index = 0 });

            // Variation indices map onto GTA variant letters, so they must be a
            // dense 0..n-1 run. Re-index rather than refusing to open.
            for (var i = 0; i < asset.Variations.Count; i++) asset.Variations[i].Index = i;

            if (asset.Variations.Count > 26)
            {
                asset.Variations = asset.Variations.Take(26).ToList();
                notes.Add($"'{asset.Name}' had more than 26 variations; the extras were dropped "
                          + "because GTA addresses variations by the letters a-z.");
            }

            foreach (var variation in asset.Variations)
            {
                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (var layer in variation.Layers)
                {
                    if (!ids.Add(layer.Id)) layer.Id = Guid.NewGuid().ToString("N");
                    if (!BlendModes.IsKnown(layer.BlendMode)) layer.BlendMode = null;
                    if (layer.Opacity is < 0 or > 1)
                        layer.Opacity = Math.Clamp(layer.Opacity, 0, 1);
                }

                // A parent id pointing at a layer that is not a group (or not
                // present) would strand the layer out of the tree.
                var groups = variation.Layers
                    .Where(l => l.Kind == LayerKind.Group)
                    .Select(l => l.Id)
                    .ToHashSet(StringComparer.Ordinal);

                foreach (var layer in variation.Layers)
                    if (layer.ParentId is not null && !groups.Contains(layer.ParentId))
                        layer.ParentId = null;
            }
        }

        if (document.Settings.TextureSize is not (256 or 512 or 1024 or 2048))
            document.Settings.TextureSize = 512;

        if (document.Export.History.Count > 20)
            document.Export.History = document.Export.History.Take(20).ToList();
    }

    // ------------------------------------------------------------------
    // JSON readers that never throw on a shape they did not expect
    // ------------------------------------------------------------------

    private static string? ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.String
            ? e.GetString()
            : null;

    private static int ReadInt(JsonElement root, string name, int fallback) =>
        root.TryGetProperty(name, out var e) && e.TryGetInt32(out var value) ? value : fallback;

    private static T ReadEnum<T>(JsonElement root, string name, T fallback) where T : struct, Enum
    {
        var text = ReadString(root, name);
        if (text is not null && Enum.TryParse<T>(text, ignoreCase: true, out var parsed)) return parsed;
        if (root.TryGetProperty(name, out var e) && e.TryGetInt32(out var number)
            && Enum.IsDefined(typeof(T), number))
        {
            return (T)Enum.ToObject(typeof(T), number);
        }
        return fallback;
    }

    private static List<TextureVariation> ReadVariations(JsonElement root)
    {
        if (!root.TryGetProperty("variations", out var array) || array.ValueKind != JsonValueKind.Array)
            return new List<TextureVariation>();

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
        };

        try
        {
            return array.Deserialize<List<TextureVariation>>(options) ?? new List<TextureVariation>();
        }
        catch (JsonException)
        {
            // A malformed variation array costs the layers, not the project.
            return new List<TextureVariation>();
        }
    }
}

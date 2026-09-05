using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bitirim.Clothing.Editor.Projects;

/// <summary>
/// The one set of serializer options for project documents.
/// </summary>
/// <remarks>
/// Saving, migration, recovery snapshots and undo snapshots all compare or
/// round-trip the same text. If any of them used different options, a snapshot
/// could differ from the file on disk purely by formatting and the recovery
/// prompt would fire after a perfectly clean shutdown.
/// </remarks>
public static class ProjectSerialization
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Serialize(ClothingProject document) =>
        JsonSerializer.Serialize(document, Options);

    public static ClothingProject Deserialize(string json) =>
        JsonSerializer.Deserialize<ClothingProject>(json, Options)
        ?? throw new Infrastructure.EditorException(
            "project_corrupt", "The project document could not be read.");

    /// <summary>A detached copy, safe to mutate without touching the original.</summary>
    public static ClothingProject Clone(ClothingProject document) =>
        Deserialize(Serialize(document));
}

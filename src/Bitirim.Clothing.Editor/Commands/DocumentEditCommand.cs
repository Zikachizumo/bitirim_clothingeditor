using Bitirim.Clothing.Editor.Projects;

namespace Bitirim.Clothing.Editor.Commands;

/// <summary>
/// A document edit, recorded as the state before and after.
/// </summary>
/// <remarks>
/// The UI sends whole documents rather than deltas, so this is the shape every
/// edit actually arrives in -- layers, transforms, variations, garments,
/// export settings. Recording both sides means undo restores exactly what was
/// there, including anything the edit touched incidentally.
///
/// Snapshots are the serialised document, not the live object: a live reference
/// would be mutated by the next edit and quietly stop describing the past.
/// A 512x512 texture's <em>pixels</em> are never in here -- they live in
/// <c>textures/</c> and layers reference them -- so a snapshot is a few
/// kilobytes of JSON even for a heavy project.
/// </remarks>
public sealed class DocumentEditCommand : IEditorCommand
{
    private readonly Action<ClothingProject> _apply;
    private readonly string _before;
    private string _after;

    /// <summary>
    /// Commands with the same non-null key, close together in time, are one
    /// gesture: a brush stroke, a dragged slider, a field being typed into.
    /// </summary>
    public string? MergeKey { get; }

    public DocumentEditCommand(
        string label,
        string beforeJson,
        string afterJson,
        Action<ClothingProject> apply,
        string? mergeKey = null)
    {
        Label = label;
        _before = beforeJson;
        _after = afterJson;
        _apply = apply;
        MergeKey = mergeKey;
    }

    public string Label { get; private set; }

    public void Execute() => _apply(Deserialize(_after));

    public void Undo() => _apply(Deserialize(_before));

    public bool TryMerge(IEditorCommand next)
    {
        if (next is not DocumentEditCommand other) return false;
        if (MergeKey is null || other.MergeKey is null) return false;
        if (!string.Equals(MergeKey, other.MergeKey, StringComparison.Ordinal)) return false;

        // Keep this command's "before" -- that is the state the whole gesture
        // started from -- and take the newest "after".
        _after = other._after;
        Label = other.Label;
        return true;
    }

    private static ClothingProject Deserialize(string json) =>
        ProjectSerialization.Deserialize(json);
}

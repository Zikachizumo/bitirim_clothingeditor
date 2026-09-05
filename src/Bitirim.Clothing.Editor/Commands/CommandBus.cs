using Bitirim.Clothing.Editor.Infrastructure;

namespace Bitirim.Clothing.Editor.Commands;

/// <summary>
/// One reversible edit.
/// </summary>
/// <remarks>
/// <see cref="Execute"/> is called both when the edit first happens and when it
/// is redone, so it must be idempotent with respect to its own recorded state
/// rather than reading whatever happens to be current.
/// </remarks>
public interface IEditorCommand
{
    /// <summary>Shown in the Edit menu, e.g. "Undo Move layer".</summary>
    string Label { get; }

    void Execute();

    void Undo();

    /// <summary>
    /// Folds a following command into this one when both are part of a single
    /// continuous gesture -- a brush stroke, a slider drag, a typed field.
    /// </summary>
    /// <returns>True when <paramref name="next"/> was absorbed and must not be pushed.</returns>
    bool TryMerge(IEditorCommand next);
}

/// <summary>
/// The undo/redo stack.
/// </summary>
/// <remarks>
/// Every project mutation in the application runs through here, which is what
/// makes undo complete rather than a per-feature afterthought: a feature added
/// later is undoable by construction because there is no other way to change
/// the document.
///
/// Depth is bounded (Settings &gt; Editor &gt; Undo depth). Dropping the oldest
/// entry is the correct trade: an editing session that has run for hours must
/// not grow without limit, and edits that old are not what anyone reaches for.
/// </remarks>
public sealed class CommandBus
{
    private readonly LogService _log;
    private readonly List<IEditorCommand> _undo = new();
    private readonly List<IEditorCommand> _redo = new();

    private DateTimeOffset _lastPush = DateTimeOffset.MinValue;

    public CommandBus(LogService log, int depth = 100)
    {
        _log = log;
        Depth = Math.Clamp(depth, 10, 1000);
    }

    public int Depth { get; set; }

    /// <summary>
    /// How long two same-gesture commands may be apart and still merge.
    /// </summary>
    /// <remarks>
    /// Long enough to cover a slow drag between pointer events, short enough
    /// that coming back to the same slider a minute later starts a new entry.
    /// </remarks>
    public TimeSpan MergeWindow { get; set; } = TimeSpan.FromMilliseconds(1200);

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public string? UndoLabel => _undo.Count > 0 ? _undo[^1].Label : null;
    public string? RedoLabel => _redo.Count > 0 ? _redo[^1].Label : null;

    public int UndoCount => _undo.Count;
    public int RedoCount => _redo.Count;

    /// <summary>
    /// Runs a command and records it.
    /// </summary>
    /// <param name="alreadyApplied">
    /// True when the caller has already put the new state in place -- the usual
    /// case for edits that arrive from the UI as a finished document. The
    /// command is then recorded without being executed a second time.
    /// </param>
    public void Run(IEditorCommand command, bool alreadyApplied = false)
    {
        if (!alreadyApplied) command.Execute();

        // A new edit invalidates the redo branch. This is the standard linear
        // model and it is what every tool the user already knows does.
        _redo.Clear();

        var now = DateTimeOffset.UtcNow;
        if (_undo.Count > 0 && now - _lastPush <= MergeWindow && _undo[^1].TryMerge(command))
        {
            _lastPush = now;
            return;
        }

        _undo.Add(command);
        _lastPush = now;

        while (_undo.Count > Depth) _undo.RemoveAt(0);
    }

    /// <summary>Reverses the most recent command. Returns its label, or null.</summary>
    public string? Undo()
    {
        if (_undo.Count == 0) return null;

        var command = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);

        try
        {
            command.Undo();
        }
        catch (Exception ex)
        {
            // A command that cannot reverse itself would leave the stack lying
            // about what it can do, so drop the whole history rather than keep
            // offering undos that no longer line up with the document.
            _log.Error($"Undo failed for '{command.Label}'; the history was cleared.", ex);
            Clear();
            throw new EditorException("undo_failed",
                $"'{command.Label}' could not be undone, so the undo history was cleared.",
                "Your document is unchanged.");
        }

        _redo.Add(command);
        _lastPush = DateTimeOffset.MinValue;
        return command.Label;
    }

    /// <summary>Re-applies the most recently undone command. Returns its label, or null.</summary>
    public string? Redo()
    {
        if (_redo.Count == 0) return null;

        var command = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);

        command.Execute();
        _undo.Add(command);
        _lastPush = DateTimeOffset.MinValue;
        return command.Label;
    }

    /// <summary>Drops the whole history. Called when the open document changes.</summary>
    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        _lastPush = DateTimeOffset.MinValue;
    }

    /// <summary>The most recent entries, newest first, for the history panel.</summary>
    public IReadOnlyList<string> RecentLabels(int max = 25) =>
        _undo.AsEnumerable().Reverse().Take(max).Select(c => c.Label).ToList();
}

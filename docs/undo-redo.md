# Undo / Redo

Undo covers **every** edit in the application, because there is only one way to
change a project document and it goes through the command stack.

```
UI edit ──► project.update ──► AppSession.Apply ──► CommandBus.Run
                                    │                     │
                              document replaced      before/after recorded
```

`AppSession.Apply` is the only method that swaps the open document. A feature
added tomorrow is undoable the moment it is written, not because anyone
remembered to wire it up.

## The command

```csharp
public interface IEditorCommand
{
    string Label { get; }     // "Add fill layer"
    void Execute();
    void Undo();
    bool TryMerge(IEditorCommand next);
}
```

The concrete implementation is `DocumentEditCommand`, which records the
serialised document **before** and **after** the edit.

### Why snapshots rather than deltas

The UI already sends whole documents — that is the shape an edit arrives in.
Recording both sides means undo restores exactly what was there, including
anything the edit touched incidentally, and it cannot drift out of step with
the document the way a hand-written inverse operation can.

The cost is bounded because **pixels are not in the document**. Texture data
lives in `textures/` and layers reference it; brush strokes are a few hundred
floats. A heavy project's snapshot is a few kilobytes of JSON.

Snapshots are text, not live objects. A live reference would be mutated by the
next edit and quietly stop describing the past — there is a test for exactly
that (`UNDO_TEST_a_snapshot_is_detached_from_the_live_document`).

## Merging

A dragged slider fires dozens of updates. Each would be its own undo step
unless they are folded together, so an edit may carry a **merge key**:

| Gesture | Key |
|---|---|
| Opacity slider | `opacity:<layerId>` |
| Dragging a layer | `transform:<layerId>` |
| Typing in a text layer | `text:<layerId>` |
| Drawable index stepper | `drawable:<assetId>` |
| Export settings fields | `export` |

Two commands merge when the keys match **and** they arrive within
`MergeWindow` (1.2 s). The merged entry keeps the *first* command's "before"
and the *latest* "after", so one undo reverses the whole gesture.

A brush stroke deliberately has **no** merge key: each stroke is its own undo
step, which is what everyone expects from a paint tool.

## Depth

Bounded by Settings → Editor → Undo depth (default 100, range 10–1000). The
oldest entry drops off the end. An editing session that runs for hours must not
grow without limit, and edits that old are not what anyone reaches for.

## Redo

Linear. A new edit clears the redo branch, which is what every tool the user
already knows does.

## What resets the history

- Opening, creating or closing a project
- Recovering a project from a crash snapshot
- Setting a garment's base asset (the files on disk changed underneath)
- An `Undo()` that throws — the stack is cleared rather than left lying about
  what it can do, and the user is told

## What is *not* on the stack

Navigation. Switching garment, variation or tab is not an edit and does not
mark the project dirty. Putting it on the stack would bury real edits under
clicks.

## Shortcuts

| Action | Default |
|---|---|
| Undo | `Ctrl+Z` |
| Redo | `Ctrl+Y` |
| Redo (alternate) | `Ctrl+Shift+Z` |

All three are marked *global*, so they work while a text field has focus.
Rebindable in Settings → Editor → Keyboard shortcuts.

## Surfacing

- The Edit menu shows the label: *Undo Brush stroke*, *Redo Add fill layer*.
- The status bar shows the current depth.
- Inspector → Project shows the next undo and redo labels.
- The status line reports what happened: "Undid Brush stroke".

## Tests

`CommandAndRecoveryTests` covers: reversing one edit, several in order, redo,
the redo branch being discarded, merging and not-merging, depth limiting,
clearing, label ordering, layer edits reversing exactly, and snapshot
detachment.

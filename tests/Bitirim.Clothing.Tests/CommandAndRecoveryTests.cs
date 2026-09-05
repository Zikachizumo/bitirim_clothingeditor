using Bitirim.Clothing.Editor.Commands;
using Bitirim.Clothing.Editor.Infrastructure;
using Bitirim.Clothing.Editor.Projects;
using Xunit;

namespace Bitirim.Clothing.Tests;

/// <summary>
/// The undo stack and crash recovery.
/// </summary>
/// <remarks>
/// Both are things a user only notices when they fail, and both lose work when
/// they do, which is why they get their own suite rather than a smoke test.
/// </remarks>
public sealed class CommandAndRecoveryTests : IDisposable
{
    private readonly string _root;
    private readonly LogService _log = new();
    private readonly ProjectService _projects;

    public CommandAndRecoveryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bcc_cmdtest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
        _projects = new ProjectService(_log);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    /// <summary>A document holder standing in for the session's open project.</summary>
    private sealed class Holder
    {
        public ClothingProject Document { get; set; } = new() { Name = "Test" };

        public DocumentEditCommand Edit(string label, Action<ClothingProject> mutate,
            string? mergeKey = null)
        {
            var before = ProjectSerialization.Serialize(Document);
            var next = ProjectSerialization.Clone(Document);
            mutate(next);
            var after = ProjectSerialization.Serialize(next);

            Document = next;
            return new DocumentEditCommand(label, before, after, d => Document = d, mergeKey);
        }
    }

    // ------------------------------------------------------------------
    // undo / redo
    // ------------------------------------------------------------------

    [Fact]
    public void UNDO_TEST_a_fresh_stack_offers_nothing()
    {
        var bus = new CommandBus(_log);

        Assert.False(bus.CanUndo);
        Assert.False(bus.CanRedo);
        Assert.Null(bus.UndoLabel);
        Assert.Null(bus.Undo());
        Assert.Null(bus.Redo());
    }

    [Fact]
    public void UNDO_TEST_reverses_a_single_edit()
    {
        var holder = new Holder();
        var bus = new CommandBus(_log);

        bus.Run(holder.Edit("Rename", d => d.Name = "Changed"), alreadyApplied: true);
        Assert.Equal("Changed", holder.Document.Name);

        Assert.Equal("Rename", bus.Undo());
        Assert.Equal("Test", holder.Document.Name);
    }

    [Fact]
    public void REDO_TEST_puts_the_edit_back()
    {
        var holder = new Holder();
        var bus = new CommandBus(_log);

        bus.Run(holder.Edit("Rename", d => d.Name = "Changed"), alreadyApplied: true);
        bus.Undo();

        Assert.Equal("Rename", bus.Redo());
        Assert.Equal("Changed", holder.Document.Name);
    }

    [Fact]
    public void UNDO_TEST_walks_back_through_several_edits_in_order()
    {
        var holder = new Holder();
        var bus = new CommandBus(_log);

        bus.Run(holder.Edit("One", d => d.Name = "1"), alreadyApplied: true);
        bus.Run(holder.Edit("Two", d => d.Name = "2"), alreadyApplied: true);
        bus.Run(holder.Edit("Three", d => d.Name = "3"), alreadyApplied: true);

        Assert.Equal("Three", bus.Undo());
        Assert.Equal("2", holder.Document.Name);
        Assert.Equal("Two", bus.Undo());
        Assert.Equal("1", holder.Document.Name);
        Assert.Equal("One", bus.Undo());
        Assert.Equal("Test", holder.Document.Name);
        Assert.False(bus.CanUndo);
    }

    [Fact]
    public void REDO_TEST_a_new_edit_discards_the_redo_branch()
    {
        var holder = new Holder();
        var bus = new CommandBus(_log);

        bus.Run(holder.Edit("One", d => d.Name = "1"), alreadyApplied: true);
        bus.Undo();
        Assert.True(bus.CanRedo);

        bus.Run(holder.Edit("Different", d => d.Name = "other"), alreadyApplied: true);

        Assert.False(bus.CanRedo);
        Assert.Equal("other", holder.Document.Name);
    }

    [Fact]
    public void UNDO_TEST_merges_one_continuous_gesture_into_one_entry()
    {
        var holder = new Holder();
        var bus = new CommandBus(_log);

        // A dragged slider sends many updates; undo has to treat them as one.
        for (var i = 1; i <= 8; i++)
        {
            var opacity = i / 10.0;
            bus.Run(holder.Edit("Change opacity",
                d => d.Variations[0].Layers.Clear(), $"opacity:layerA"), alreadyApplied: true);
            _ = opacity;
        }

        Assert.Equal(1, bus.UndoCount);
    }

    [Fact]
    public void UNDO_TEST_does_not_merge_edits_with_different_gestures()
    {
        var holder = new Holder();
        var bus = new CommandBus(_log);

        bus.Run(holder.Edit("A", d => d.Name = "a", "opacity:one"), alreadyApplied: true);
        bus.Run(holder.Edit("B", d => d.Name = "b", "opacity:two"), alreadyApplied: true);

        Assert.Equal(2, bus.UndoCount);
    }

    [Fact]
    public void UNDO_TEST_does_not_merge_when_no_gesture_is_named()
    {
        var holder = new Holder();
        var bus = new CommandBus(_log);

        bus.Run(holder.Edit("Stroke", d => d.Name = "a"), alreadyApplied: true);
        bus.Run(holder.Edit("Stroke", d => d.Name = "b"), alreadyApplied: true);

        // Two brush strokes are two undo steps, which is what anyone expects.
        Assert.Equal(2, bus.UndoCount);
    }

    [Fact]
    public void UNDO_TEST_a_merged_gesture_reverses_to_where_it_started()
    {
        var holder = new Holder();
        var bus = new CommandBus(_log);

        bus.Run(holder.Edit("Drag", d => d.Name = "step1", "drag:x"), alreadyApplied: true);
        bus.Run(holder.Edit("Drag", d => d.Name = "step2", "drag:x"), alreadyApplied: true);
        bus.Run(holder.Edit("Drag", d => d.Name = "step3", "drag:x"), alreadyApplied: true);

        bus.Undo();

        Assert.Equal("Test", holder.Document.Name);
    }

    [Fact]
    public void UNDO_TEST_history_is_bounded_by_the_configured_depth()
    {
        var holder = new Holder();
        var bus = new CommandBus(_log, depth: 10);

        for (var i = 0; i < 40; i++)
            bus.Run(holder.Edit($"Edit {i}", d => d.Name = $"n{i}"), alreadyApplied: true);

        Assert.Equal(10, bus.UndoCount);
        Assert.Equal("Edit 39", bus.UndoLabel);
    }

    [Fact]
    public void UNDO_TEST_clearing_the_history_leaves_the_document_alone()
    {
        var holder = new Holder();
        var bus = new CommandBus(_log);

        bus.Run(holder.Edit("Rename", d => d.Name = "Changed"), alreadyApplied: true);
        bus.Clear();

        Assert.False(bus.CanUndo);
        Assert.False(bus.CanRedo);
        Assert.Equal("Changed", holder.Document.Name);
    }

    [Fact]
    public void UNDO_TEST_recent_labels_read_newest_first()
    {
        var holder = new Holder();
        var bus = new CommandBus(_log);

        bus.Run(holder.Edit("First", d => d.Name = "1"), alreadyApplied: true);
        bus.Run(holder.Edit("Second", d => d.Name = "2"), alreadyApplied: true);

        Assert.Equal(new[] { "Second", "First" }, bus.RecentLabels());
    }

    [Fact]
    public void UNDO_TEST_a_layer_edit_reverses_exactly()
    {
        var holder = new Holder();
        var bus = new CommandBus(_log);

        bus.Run(holder.Edit("Add layer", d => d.Variations[0].Layers.Add(new TextureLayer
        {
            Id = "l1", Name = "Fill 1", Kind = LayerKind.Fill, Color = "#ff0000",
        })), alreadyApplied: true);

        Assert.Single(holder.Document.Variations[0].Layers);

        bus.Undo();
        Assert.Empty(holder.Document.Variations[0].Layers);

        bus.Redo();
        Assert.Equal("Fill 1", holder.Document.Variations[0].Layers[0].Name);
    }

    [Fact]
    public void UNDO_TEST_a_snapshot_is_detached_from_the_live_document()
    {
        var holder = new Holder();
        var bus = new CommandBus(_log);

        bus.Run(holder.Edit("Add layer",
            d => d.Variations[0].Layers.Add(new TextureLayer { Id = "l1", Name = "Original" })),
            alreadyApplied: true);

        // Mutating the live document must not rewrite what undo will restore.
        holder.Document.Variations[0].Layers[0].Name = "Tampered";
        bus.Redo();
        bus.Undo();
        bus.Redo();

        Assert.Equal("Original", holder.Document.Variations[0].Layers[0].Name);
    }

    // ------------------------------------------------------------------
    // recovery
    // ------------------------------------------------------------------

    private RecoveryService NewRecovery() =>
        new(_log, Path.Combine(_root, "recovery_" + Guid.NewGuid().ToString("N")[..6]));

    [Fact]
    public void RECOVERY_TEST_nothing_pending_on_a_clean_start()
    {
        Assert.Empty(NewRecovery().Pending());
    }

    [Fact]
    public void RECOVERY_TEST_a_snapshot_that_matches_disk_is_not_offered()
    {
        var recovery = NewRecovery();
        var project = _projects.Create(_root, new ClothingProject { Name = "Clean" });

        recovery.Snapshot(project, File.ReadAllText(project.ProjectJsonPath));

        // A clean shutdown that merely skipped the last step must not produce a
        // scary dialog about work that was never lost.
        Assert.Empty(recovery.Pending());
    }

    [Fact]
    public void RECOVERY_TEST_a_snapshot_that_differs_is_offered()
    {
        var recovery = NewRecovery();
        var project = _projects.Create(_root, new ClothingProject { Name = "Dirty" });

        project.Document.Name = "Unsaved edit";
        recovery.Snapshot(project, ProjectSerialization.Serialize(project.Document));

        var pending = Assert.Single(recovery.Pending());
        Assert.Equal("Unsaved edit", pending.Name);
        Assert.Equal(project.Directory, pending.Directory);
    }

    [Fact]
    public void RECOVERY_TEST_clearing_removes_the_offer()
    {
        var recovery = NewRecovery();
        var project = _projects.Create(_root, new ClothingProject { Name = "Cleared" });

        project.Document.Name = "Unsaved";
        recovery.Snapshot(project, ProjectSerialization.Serialize(project.Document));
        Assert.Single(recovery.Pending());

        recovery.Clear(project);
        Assert.Empty(recovery.Pending());
    }

    [Fact]
    public void RECOVERY_TEST_restoring_writes_the_snapshot_and_keeps_the_current_file()
    {
        var recovery = NewRecovery();
        var project = _projects.Create(_root, new ClothingProject { Name = "Restorable" });
        var onDiskBefore = File.ReadAllText(project.ProjectJsonPath);

        project.Document.Name = "Recovered name";
        recovery.Snapshot(project, ProjectSerialization.Serialize(project.Document));

        var record = Assert.Single(recovery.Pending());
        recovery.Restore(record);

        Assert.Contains("Recovered name", File.ReadAllText(project.ProjectJsonPath));

        var backups = Directory.GetDirectories(Path.Combine(project.Directory, "backups"))
            .Where(d => Path.GetFileName(d).StartsWith("before-recovery-"))
            .ToList();
        var kept = Assert.Single(backups);
        Assert.Equal(onDiskBefore, File.ReadAllText(Path.Combine(kept, "project.json")));
    }

    [Fact]
    public void RECOVERY_TEST_restoring_clears_the_record()
    {
        var recovery = NewRecovery();
        var project = _projects.Create(_root, new ClothingProject { Name = "Once" });

        project.Document.Name = "Changed";
        recovery.Snapshot(project, ProjectSerialization.Serialize(project.Document));
        recovery.Restore(recovery.Pending()[0]);

        Assert.Empty(recovery.Pending());
    }

    [Fact]
    public void RECOVERY_TEST_a_record_for_a_deleted_project_is_discarded()
    {
        var recovery = NewRecovery();
        var project = _projects.Create(_root, new ClothingProject { Name = "Doomed" });

        project.Document.Name = "Changed";
        recovery.Snapshot(project, ProjectSerialization.Serialize(project.Document));
        Directory.Delete(project.Directory, recursive: true);

        Assert.Empty(recovery.Pending());
    }

    [Fact]
    public void RECOVERY_TEST_an_unreadable_record_is_discarded_quietly()
    {
        var directory = Path.Combine(_root, "recovery_bad");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "broken.json"), "{ not json");

        var recovery = new RecoveryService(_log, directory);

        Assert.Empty(recovery.Pending());
        Assert.False(File.Exists(Path.Combine(directory, "broken.json")));
    }

    [Fact]
    public void RECOVERY_TEST_keys_are_stable_for_the_same_project()
    {
        var project = _projects.Create(_root, new ClothingProject { Name = "Stable" });
        var reopened = _projects.Open(project.Directory);

        Assert.Equal(RecoveryService.KeyFor(project), RecoveryService.KeyFor(reopened));
    }

    [Fact]
    public void RECOVERY_TEST_two_projects_get_different_keys()
    {
        var a = _projects.Create(_root, new ClothingProject { Name = "A" });
        var b = _projects.Create(_root, new ClothingProject { Name = "B" });

        Assert.NotEqual(RecoveryService.KeyFor(a), RecoveryService.KeyFor(b));
    }
}

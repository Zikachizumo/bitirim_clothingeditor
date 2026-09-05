using Bitirim.Clothing.Core.Rage;
using Bitirim.Clothing.Editor.Ai;
using Bitirim.Clothing.Editor.Commands;
using Bitirim.Clothing.Editor.Infrastructure;
using Bitirim.Clothing.Editor.Library;
using Bitirim.Clothing.Editor.Projects;
using Bitirim.Clothing.Editor.Settings;
using Bitirim.Clothing.FiveFury;
using Bitirim.Clothing.Textures;
using Bitirim.Clothing.Validation;

namespace Bitirim.Clothing.Desktop.Host;

/// <summary>
/// Everything the running application holds onto.
/// </summary>
/// <remarks>
/// Deliberately a plain object rather than a DI container: the graph is small,
/// fixed, and created once at startup. The asset backend is created lazily so a
/// missing Python runtime degrades to a clear message rather than a crash on
/// launch -- the UI still opens and reports what is unavailable.
/// </remarks>
public sealed class AppSession : IAsyncDisposable
{
    private readonly SemaphoreSlim _backendGate = new(1, 1);
    private IRageAssetBackend? _backend;
    private BackendCapabilities? _capabilities;
    private string? _backendFailure;

    public AppSession()
    {
        Log = new LogService();
        Settings = new SettingsService(Log);
        Projects = new ProjectService(Log);
        Library = new AssetLibraryService(Log);
        Validator = new ValidationEngine();
        ProjectValidator = new Editor.Validation.ProjectValidator(Validator);
        Encoder = new BcnTextureEncoder();
        Recovery = new RecoveryService(Log);
        Thumbnails = new ThumbnailService(Log);
        Commands = new CommandBus(Log, Settings.Current.UndoDepth);

        // Constructing this reads local configuration only. No key, no network
        // call, nothing that can fail -- the application still starts without a
        // Gemini key, and every non-AI feature works unchanged.
        Ai = new AiProviderRegistry(Settings, Log);
    }

    public LogService Log { get; }
    public SettingsService Settings { get; }
    public ProjectService Projects { get; }
    public AssetLibraryService Library { get; }
    public ValidationEngine Validator { get; }
    public Editor.Validation.ProjectValidator ProjectValidator { get; }
    public BcnTextureEncoder Encoder { get; }
    public RecoveryService Recovery { get; }
    public ThumbnailService Thumbnails { get; }

    /// <summary>AI texture providers. Only Gemini is implemented.</summary>
    public AiProviderRegistry Ai { get; }

    /// <summary>The undo/redo stack. Cleared whenever the open document changes.</summary>
    public CommandBus Commands { get; }

    public OpenProject? Current { get; set; }
    public bool Dirty { get; set; }

    /// <summary>An export in progress, so the wizard's Cancel button has something to pull.</summary>
    public CancellationTokenSource? RunningExport { get; set; }

    /// <summary>
    /// A texture generation in progress.
    /// </summary>
    /// <remarks>
    /// Held here rather than in the dialog so a second Generate cannot start
    /// while the first is in flight, and so closing the dialog mid-request
    /// leaves nothing dangling.
    /// </remarks>
    public CancellationTokenSource? RunningAi { get; set; }

    /// <summary>When the open project was last written by autosave.</summary>
    public DateTimeOffset LastAutosave { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Replaces the open document, recording the change on the undo stack.
    /// </summary>
    /// <remarks>
    /// This is the only way the document changes once a project is open, which
    /// is what makes undo cover every feature rather than a chosen few. A
    /// feature added later is undoable because there is no other door.
    /// </remarks>
    public void Apply(ClothingProject next, string label, string? mergeKey = null)
    {
        var project = RequireProject();
        var before = ProjectSerialization.Serialize(project.Document);
        var after = ProjectSerialization.Serialize(next);

        if (string.Equals(before, after, StringComparison.Ordinal)) return;

        Current = project with { Document = next };
        Dirty = true;

        Commands.Depth = Settings.Current.UndoDepth;
        Commands.Run(
            new DocumentEditCommand(label, before, after, SetDocument, mergeKey),
            alreadyApplied: true);
    }

    /// <summary>Puts a document in place without touching the undo stack.</summary>
    private void SetDocument(ClothingProject document)
    {
        if (Current is null) return;
        Current = Current with { Document = document };
        Dirty = true;
    }

    /// <summary>Called whenever the open project is replaced or closed.</summary>
    public void ResetHistory()
    {
        Commands.Clear();
        Commands.Depth = Settings.Current.UndoDepth;
    }

    /// <summary>Refreshes the crash-recovery snapshot for the open project.</summary>
    public void SnapshotForRecovery()
    {
        if (Current is null) return;
        Recovery.Snapshot(Current, ProjectSerialization.Serialize(Current.Document));
    }

    /// <summary>Why the asset backend is unavailable, if it is.</summary>
    public string? BackendFailure => _backendFailure;

    /// <summary>
    /// The asset backend, started on first use.
    /// </summary>
    /// <exception cref="EditorException">
    /// When the bundled runtime is missing. The message names what is absent so
    /// the user can act on it.
    /// </exception>
    public async Task<IRageAssetBackend> BackendAsync(CancellationToken ct = default)
    {
        if (_backend is not null) return _backend;

        await _backendGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_backend is not null) return _backend;

            var python = Paths.BundledPython;
            var service = Paths.AssetService;

            if (!File.Exists(python))
            {
                _backendFailure = "The bundled asset runtime is missing from this installation.";
                Log.Error($"Python runtime not found at {python}");
                throw new EditorException("backend_missing", _backendFailure,
                    "Reinstall Bitirim Clothing Creator, or set BCC_PYTHON for a development build.");
            }

            if (!File.Exists(service))
            {
                _backendFailure = "The bundled asset service is missing from this installation.";
                Log.Error($"Asset service not found at {service}");
                throw new EditorException("backend_missing", _backendFailure,
                    "Reinstall Bitirim Clothing Creator, or set BCC_ASSETSERVICE for a development build.");
            }

            var backend = new FiveFuryRageAssetBackend(python, service);
            _capabilities = await backend.GetCapabilitiesAsync(ct).ConfigureAwait(false);
            _backend = backend;
            _backendFailure = null;

            Log.Info($"Asset backend ready: {_capabilities.Backend} {_capabilities.BackendVersion} "
                     + $"on Python {_capabilities.Python} ({_capabilities.ContractVersion})");
            return backend;
        }
        finally
        {
            _backendGate.Release();
        }
    }

    /// <summary>
    /// Backend capabilities, or null when the backend could not start.
    /// </summary>
    /// <remarks>
    /// The UI binds affordances to these flags. A capability we have not proven
    /// is reported false, which makes it structurally impossible to offer the
    /// feature as working -- see docs/architecture.md.
    /// </remarks>
    public async Task<BackendCapabilities?> TryCapabilitiesAsync(CancellationToken ct = default)
    {
        if (_capabilities is not null) return _capabilities;
        try
        {
            await BackendAsync(ct).ConfigureAwait(false);
            return _capabilities;
        }
        catch (EditorException)
        {
            return null;
        }
        catch (Exception ex)
        {
            _backendFailure = "The asset engine could not be started.";
            Log.Error("Asset backend startup failed", ex);
            return null;
        }
    }

    public OpenProject RequireProject() =>
        Current ?? throw new EditorException("no_project", "No project is open.");

    public async ValueTask DisposeAsync()
    {
        if (_backend is not null) await _backend.DisposeAsync();
        _backendGate.Dispose();
    }
}

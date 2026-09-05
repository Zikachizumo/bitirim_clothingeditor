using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bitirim.Clothing.Core.Naming;
using Bitirim.Clothing.Editor.Export;
using Bitirim.Clothing.Editor.Infrastructure;
using Bitirim.Clothing.Editor.Library;
using Bitirim.Clothing.Editor.Projects;
using Bitirim.Clothing.Export;
using Bitirim.Clothing.Textures;
using Bitirim.Clothing.Validation;
using Microsoft.Win32;

namespace Bitirim.Clothing.Desktop.Host;

/// <summary>
/// Registers every operation the UI can call.
/// </summary>
/// <remarks>
/// This is the entire surface between the React layer and the application. The
/// UI holds no business logic and never touches the filesystem or the asset
/// backend directly.
/// </remarks>
public static class HostOperations
{
    public static void Register(HostBridge bridge, AppSession session, Func<System.Windows.Window?> window)
    {
        RegisterApp(bridge, session);
        RegisterSettings(bridge, session);
        RegisterProject(bridge, session, window);
        RegisterLibrary(bridge, session, window);
        RegisterAsset(bridge, session);
        RegisterExport(bridge, session, window);
        RegisterShell(bridge, session, window);
        AiOperations.Register(bridge, session);
    }

    // ------------------------------------------------------------------
    // app
    // ------------------------------------------------------------------

    private static void RegisterApp(HostBridge bridge, AppSession session)
    {
        bridge.On("app.info", _ => new
        {
            name = "Bitirim Clothing Creator",
            version = Version(),
            protocol = HostBridge.ProtocolVersion,
            portable = Paths.IsPortable,
            userDataRoot = Paths.Root,
            os = Environment.OSVersion.VersionString,
            developerMode = session.Settings.Current.DeveloperMode,
        });

        bridge.On("app.capabilities", async _ =>
        {
            var caps = await session.TryCapabilitiesAsync();
            if (caps is null)
            {
                return new
                {
                    available = false,
                    reason = session.BackendFailure
                             ?? "The asset engine is unavailable. Real GTA assets cannot be read or written.",
                    capabilities = new Dictionary<string, bool>(),
                };
            }

            return new
            {
                available = true,
                reason = (string?)null,
                backend = caps.Backend,
                backendVersion = caps.BackendVersion,
                python = caps.Python,
                contract = caps.ContractVersion,
                capabilities = new Dictionary<string, bool>
                {
                    ["readYdd"] = caps.ReadYdd,
                    ["readYtd"] = caps.ReadYtd,
                    ["readYmt"] = caps.ReadYmt,
                    ["writeYtd"] = caps.WriteYtd,
                    ["writeYmt"] = caps.WriteYmt,
                    ["writeYdd"] = caps.WriteYdd,
                    ["writeYddGeometry"] = caps.WriteYddGeometry,
                    ["encodeTexture"] = true, // host-side; the backend flag refers to itself
                },
            };
        });

        bridge.On("app.components", _ => ClothingNames.All.Select(c => new
        {
            index = (int)c,
            prefix = ClothingNames.Prefix(c),
            label = ClothingNames.Label(c),
        }));

        bridge.On("log.recent", args =>
            session.Log.Recent(HostBridge.OptionalInt(args, "max", 300))
                .Select(e => new
                {
                    at = e.At,
                    level = e.Level.ToString().ToLowerInvariant(),
                    channel = e.Channel,
                    message = e.Message,
                }));

        bridge.On("log.write", args =>
        {
            var level = HostBridge.OptionalString(args, "level") ?? "info";
            var message = HostBridge.OptionalString(args, "message") ?? "";
            switch (level)
            {
                case "warn": session.Log.Warn($"[ui] {message}"); break;
                case "error": session.Log.Error($"[ui] {message}"); break;
                default: session.Log.Info($"[ui] {message}"); break;
            }
            return true;
        });
    }

    // ------------------------------------------------------------------
    // settings
    // ------------------------------------------------------------------

    private static void RegisterSettings(HostBridge bridge, AppSession session)
    {
        bridge.On("settings.get", _ => new
        {
            settings = session.Settings.PublicView(),
            // Only whether a key exists, never the key itself.
            hasAiApiKey = session.Settings.HasAiApiKey,
            paths = new
            {
                logs = Paths.Logs,
                root = Paths.Root,
                defaultProjects = session.Settings.Current.DefaultProjectFolder,
            },
        });

        bridge.On("settings.set", args =>
        {
            var patch = HostBridge.Deserialize<Dictionary<string, JsonElement>>(args, "patch");
            if (patch is null) return session.Settings.PublicView();

            var current = session.Settings.Current;
            foreach (var (key, value) in patch)
            {
                var property = typeof(Editor.Settings.AppSettings).GetProperty(key,
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (property is null || !property.CanWrite) continue;
                if (string.Equals(property.Name, nameof(Editor.Settings.AppSettings.AiApiKeyProtected),
                        StringComparison.OrdinalIgnoreCase)) continue; // never set directly

                try
                {
                    var typed = value.Deserialize(property.PropertyType, HostBridge.Json);
                    property.SetValue(current, typed);
                }
                catch (JsonException)
                {
                    session.Log.Warn($"Ignored settings value of the wrong type: {key}");
                }
            }

            session.Settings.Save();
            session.Log.Info("Settings updated.");
            return session.Settings.PublicView();
        });

        bridge.On("settings.setAiKey", args =>
        {
            session.Settings.SetAiApiKey(HostBridge.OptionalString(args, "key"));
            return new { hasAiApiKey = session.Settings.HasAiApiKey };
        });
    }

    // ------------------------------------------------------------------
    // project
    // ------------------------------------------------------------------

    private static void RegisterProject(
        HostBridge bridge, AppSession session, Func<System.Windows.Window?> window)
    {
        bridge.On("project.current", _ => Describe(session));

        bridge.On("project.recent", _ => session.Settings.Current.RecentProjects
            .Select(p => new
            {
                path = p,
                name = Path.GetFileNameWithoutExtension(p),
                exists = File.Exists(p) || Directory.Exists(p),
                modified = SafeModified(p),
            }));

        bridge.On("project.new", args =>
        {
            var document = HostBridge.Deserialize<ClothingProject>(args, "project")
                           ?? throw new EditorException("bad_args", "No project details were supplied.");

            var folder = HostBridge.OptionalString(args, "folder")
                         ?? session.Settings.Current.DefaultProjectFolder
                         ?? Paths.DefaultProjectsFolder;
            Directory.CreateDirectory(folder);

            // The wizard sends the garment's slot alongside the document,
            // because component and drawable index live on the asset in schema
            // 2 and a freshly created project has no asset list yet.
            if (document.Assets.Count == 0)
            {
                var origin = HostBridge.OptionalString(args, "baseAssetOrigin");
                document.Assets.Add(new ClothingAsset
                {
                    Name = document.Name,
                    Component = ParseComponent(HostBridge.OptionalString(args, "component"))
                                ?? PedComponent.Jbib,
                    DrawableIndex = HostBridge.OptionalInt(args, "drawableIndex", 0),
                    BaseAssetOrigin =
                        Enum.TryParse<BaseAssetOrigin>(origin, ignoreCase: true, out var parsed)
                            ? parsed
                            : BaseAssetOrigin.None,
                });
                document.ActiveAssetId = document.Assets[0].Id;
            }

            var project = session.Projects.Create(folder, document);

            // Copy the chosen base asset in so the project stays portable.
            var ydd = HostBridge.OptionalString(args, "baseYdd");
            var ytd = HostBridge.OptionalString(args, "baseYtd");
            var ymt = HostBridge.OptionalString(args, "ymtTemplate");

            if (!string.IsNullOrEmpty(ydd) && File.Exists(ydd))
            {
                project.Document.BaseYddPath = ProjectService.ImportInto(project, ydd, "assets");
                project.Document.BaseAssetOrigin = BaseAssetOrigin.Imported;
            }
            if (!string.IsNullOrEmpty(ytd) && File.Exists(ytd))
                project.Document.BaseYtdPath = ImportBaseYtd(session, project, ytd);
            if (!string.IsNullOrEmpty(ymt) && File.Exists(ymt))
                project.Document.YmtTemplatePath = ProjectService.ImportInto(project, ymt, "assets");

            session.Projects.Save(project);
            session.Current = project;
            session.Dirty = false;
            session.LastAutosave = DateTimeOffset.UtcNow;
            session.ResetHistory();
            session.Settings.AddRecentProject(project.Directory);
            return Describe(session);
        });

        bridge.On("project.open", async args =>
        {
            var path = HostBridge.OptionalString(args, "path");
            if (string.IsNullOrEmpty(path))
            {
                var dialog = new OpenFileDialog
                {
                    Title = "Open Clothing Project",
                    Filter = "Bitirim clothing project (*.bitirimclothing;project.json)"
                             + "|*.bitirimclothing;project.json|All files (*.*)|*.*",
                };
                if (dialog.ShowDialog(window()) != true) return await Task.FromResult<object?>(null);
                path = dialog.FileName;
            }

            var project = session.Projects.Open(path);
            session.Current = project;
            session.Dirty = false;
            session.LastAutosave = DateTimeOffset.UtcNow;
            session.ResetHistory();
            session.Settings.AddRecentProject(project.PackagePath ?? project.Directory);
            return Describe(session);
        });

        bridge.On("project.save", _ =>
        {
            var project = session.RequireProject();
            session.Projects.Save(project);
            session.Dirty = false;
            return Describe(session);
        });

        bridge.On("project.saveAs", args =>
        {
            var project = session.RequireProject();
            var path = HostBridge.OptionalString(args, "path");

            if (string.IsNullOrEmpty(path))
            {
                var dialog = new SaveFileDialog
                {
                    Title = "Save Project Package",
                    FileName = project.Document.Name + ProjectService.PackageExtension,
                    DefaultExt = ProjectService.PackageExtension,
                    Filter = $"Bitirim clothing project (*{ProjectService.PackageExtension})"
                             + $"|*{ProjectService.PackageExtension}",
                };
                if (dialog.ShowDialog(window()) != true) return null;
                path = dialog.FileName;
            }

            session.Projects.Save(project);
            session.Current = session.Projects.SaveAsPackage(project, path);
            session.Dirty = false;
            session.Settings.AddRecentProject(path);
            return Describe(session);
        });

        bridge.On("project.update", args =>
        {
            var project = session.RequireProject();
            var document = HostBridge.Deserialize<ClothingProject>(args, "project")
                           ?? throw new EditorException("bad_args", "No project details were supplied.");

            // Identity and provenance are the host's to manage, not the UI's.
            // Asset file paths in particular: the UI may reorder or rename
            // garments, but it never decides where their bytes live.
            document.CreatedAt = project.Document.CreatedAt;
            document.SchemaVersion = ClothingProject.CurrentSchemaVersion;
            document.YmtTemplatePath = project.Document.YmtTemplatePath;

            var known = project.Document.Assets.ToDictionary(a => a.Id, StringComparer.Ordinal);
            foreach (var asset in document.Assets)
            {
                if (!known.TryGetValue(asset.Id, out var original)) continue;
                asset.BaseAssetOrigin = original.BaseAssetOrigin;
                asset.BaseYddPath = original.BaseYddPath;
                asset.BaseYtdPath = original.BaseYtdPath;
            }

            session.Apply(
                document,
                HostBridge.OptionalString(args, "label") ?? "Edit",
                HostBridge.OptionalString(args, "mergeKey"));

            if (HostBridge.OptionalBool(args, "save", false))
            {
                session.Projects.Save(session.RequireProject());
                session.Dirty = false;
                session.Recovery.Clear(session.RequireProject());
            }

            return Describe(session);
        });

        bridge.On("project.close", _ =>
        {
            if (session.Current is not null) session.Recovery.Clear(session.Current);
            session.Current = null;
            session.Dirty = false;
            session.ResetHistory();
            return Describe(session);
        });

        // Called on a short tick by the UI. Two different jobs share it, on two
        // different clocks:
        //
        //   * the recovery snapshot runs every tick, because it costs a few
        //     kilobytes and bounds how much a crash can take;
        //   * the real save runs on the user's autosave interval, because
        //     writing over their file more often than they asked for is not
        //     ours to decide.
        //
        // Doing only the second would leave everything since the last autosave
        // unprotected, which is exactly the work a crash takes.
        bridge.On("project.autosave", _ =>
        {
            if (session.Current is null || !session.Dirty)
                return new { saved = false, snapshotted = false };

            session.SnapshotForRecovery();

            var settings = session.Settings.Current;
            var due = DateTimeOffset.UtcNow - session.LastAutosave
                      >= TimeSpan.FromMinutes(settings.AutosaveMinutes);

            if (!settings.AutosaveEnabled || !due)
                return new { saved = false, snapshotted = true, at = DateTimeOffset.Now };

            session.Projects.Save(session.Current);
            session.Dirty = false;
            session.LastAutosave = DateTimeOffset.UtcNow;
            session.Recovery.Clear(session.Current);
            session.Log.Info("Autosaved.");
            return new { saved = true, snapshotted = true, at = DateTimeOffset.Now };
        });

        // ------------------------------------------------------------------
        // undo / redo
        // ------------------------------------------------------------------

        bridge.On("edit.undo", _ =>
        {
            session.RequireProject();
            var label = session.Commands.Undo();
            if (label is null) return new { changed = false, label = (string?)null, state = Describe(session) };

            session.Log.Info($"Undo: {label}");
            return new { changed = true, label, state = Describe(session) };
        });

        bridge.On("edit.redo", _ =>
        {
            session.RequireProject();
            var label = session.Commands.Redo();
            if (label is null) return new { changed = false, label = (string?)null, state = Describe(session) };

            session.Log.Info($"Redo: {label}");
            return new { changed = true, label, state = Describe(session) };
        });

        bridge.On("edit.history", _ => new
        {
            canUndo = session.Commands.CanUndo,
            canRedo = session.Commands.CanRedo,
            undoLabel = session.Commands.UndoLabel,
            redoLabel = session.Commands.RedoLabel,
            depth = session.Commands.UndoCount,
            limit = session.Commands.Depth,
            recent = session.Commands.RecentLabels(),
        });

        // ------------------------------------------------------------------
        // crash recovery
        // ------------------------------------------------------------------

        bridge.On("recovery.list", _ => session.Recovery.Pending().Select(r => new
        {
            key = r.Key,
            name = r.Name,
            directory = r.Directory,
            packagePath = r.PackagePath,
            at = r.At,
        }));

        bridge.On("recovery.restore", args =>
        {
            var key = HostBridge.RequiredString(args, "key");
            var record = session.Recovery.Pending().FirstOrDefault(r => r.Key == key)
                         ?? throw new EditorException("not_found",
                             "That recovered project is no longer available.");

            var path = session.Recovery.Restore(record);
            var project = session.Projects.Open(path);

            session.Current = project;
            session.Dirty = false;
            session.LastAutosave = DateTimeOffset.UtcNow;
            session.ResetHistory();
            session.Settings.AddRecentProject(project.PackagePath ?? project.Directory);
            return Describe(session);
        });

        bridge.On("recovery.discard", args =>
        {
            session.Recovery.Clear(HostBridge.RequiredString(args, "key"));
            return new { discarded = true };
        });

        // ------------------------------------------------------------------
        // garments within a project
        // ------------------------------------------------------------------

        bridge.On("assets.setActive", args =>
        {
            var project = session.RequireProject();
            var id = HostBridge.RequiredString(args, "id");
            if (project.Document.Assets.All(a => a.Id != id))
                throw new EditorException("not_found", "That garment is not in this project.");

            // Switching garment is navigation, not an edit: it must not land on
            // the undo stack, and it must not mark the project dirty.
            project.Document.ActiveAssetId = id;
            return Describe(session);
        });

        bridge.On("assets.add", args =>
        {
            var project = session.RequireProject();
            var next = ProjectSerialization.Clone(project.Document);

            var asset = new ClothingAsset
            {
                Name = HostBridge.OptionalString(args, "name") ?? "New garment",
                Component = ParseComponent(HostBridge.OptionalString(args, "component"))
                            ?? PedComponent.Jbib,
                DrawableIndex = HostBridge.OptionalInt(args, "drawableIndex", 0),
            };

            var ydd = HostBridge.OptionalString(args, "ydd");
            var ytd = HostBridge.OptionalString(args, "ytd");

            if (!string.IsNullOrEmpty(ydd) && File.Exists(ydd))
            {
                asset.BaseYddPath = ProjectService.ImportInto(project, ydd, "assets");
                asset.BaseAssetOrigin = BaseAssetOrigin.Imported;

                var parsed = AssetLibraryService.ParseDrawableName(Path.GetFileNameWithoutExtension(ydd));
                if (parsed.Component is not null) asset.Component = parsed.Component.Value;
                if (parsed.Drawable is not null) asset.DrawableIndex = parsed.Drawable.Value;
                if (HostBridge.OptionalString(args, "name") is null)
                    asset.Name = Path.GetFileNameWithoutExtension(ydd);
            }

            if (!string.IsNullOrEmpty(ytd) && File.Exists(ytd))
                asset.BaseYtdPath = ImportBaseYtd(session, project, ytd);

            next.Assets.Add(asset);
            next.ActiveAssetId = asset.Id;

            session.Apply(next, $"Add garment '{asset.Name}'");
            session.Projects.Save(session.RequireProject());
            session.Dirty = false;
            return Describe(session);
        });

        bridge.On("assets.remove", args =>
        {
            var project = session.RequireProject();
            var id = HostBridge.RequiredString(args, "id");

            if (project.Document.Assets.Count <= 1)
                throw new EditorException("last_asset",
                    "A project must contain at least one garment.");

            var next = ProjectSerialization.Clone(project.Document);
            var removed = next.Assets.FirstOrDefault(a => a.Id == id)
                          ?? throw new EditorException("not_found",
                              "That garment is not in this project.");

            next.Assets.Remove(removed);
            if (next.ActiveAssetId == id) next.ActiveAssetId = next.Assets[0].Id;

            // The garment's files stay in assets/ so an undo restores a working
            // project rather than a document pointing at deleted bytes.
            session.Apply(next, $"Remove garment '{removed.Name}'");
            return Describe(session);
        });

        bridge.On("assets.setBase", async args =>
        {
            var project = session.RequireProject();
            var id = HostBridge.OptionalString(args, "id") ?? project.Document.Active.Id;
            var ydd = HostBridge.OptionalString(args, "ydd");
            var ytd = HostBridge.OptionalString(args, "ytd");

            var target = project.Document.Assets.FirstOrDefault(a => a.Id == id)
                         ?? throw new EditorException("not_found",
                             "That garment is not in this project.");

            if (!string.IsNullOrEmpty(ydd))
            {
                if (!File.Exists(ydd))
                    throw new EditorException("not_found", "That drawable no longer exists.");

                target.BaseYddPath = ProjectService.ImportInto(project, ydd, "assets");
                target.BaseAssetOrigin = BaseAssetOrigin.Imported;

                var parsed = AssetLibraryService.ParseDrawableName(Path.GetFileNameWithoutExtension(ydd));
                if (parsed.Component is not null) target.Component = parsed.Component.Value;
                if (parsed.Drawable is not null) target.DrawableIndex = parsed.Drawable.Value;
            }

            if (!string.IsNullOrEmpty(ytd))
            {
                if (!File.Exists(ytd))
                    throw new EditorException("not_found", "That texture dictionary no longer exists.");
                target.BaseYtdPath = ImportBaseYtd(session, project, ytd);
            }

            session.Projects.Save(project);
            session.Dirty = false;
            session.ResetHistory();
            await Task.CompletedTask;
            return Describe(session);
        });

        // ------------------------------------------------------------------
        // outfits
        // ------------------------------------------------------------------

        bridge.On("outfit.save", args =>
        {
            var project = session.RequireProject();
            var outfit = HostBridge.Deserialize<Outfit>(args, "outfit")
                         ?? throw new EditorException("bad_args", "No outfit was supplied.");

            var next = ProjectSerialization.Clone(project.Document);
            var existing = next.Outfits.FindIndex(o => o.Id == outfit.Id);
            if (existing >= 0) next.Outfits[existing] = outfit;
            else next.Outfits.Add(outfit);

            session.Apply(next, existing >= 0 ? $"Update outfit '{outfit.Name}'"
                                              : $"Save outfit '{outfit.Name}'");
            return Describe(session);
        });

        bridge.On("outfit.delete", args =>
        {
            var project = session.RequireProject();
            var id = HostBridge.RequiredString(args, "id");

            var next = ProjectSerialization.Clone(project.Document);
            var outfit = next.Outfits.FirstOrDefault(o => o.Id == id);
            if (outfit is null) return Describe(session);

            next.Outfits.Remove(outfit);
            session.Apply(next, $"Delete outfit '{outfit.Name}'");
            return Describe(session);
        });
    }

    private static PedComponent? ParseComponent(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        if (Enum.TryParse<PedComponent>(value, ignoreCase: true, out var parsed)) return parsed;
        if (int.TryParse(value, out var index) && Enum.IsDefined(typeof(PedComponent), index))
            return (PedComponent)index;
        if (ClothingNames.TryParsePrefix(value, out var byPrefix)) return byPrefix;
        return null;
    }

    private static object Describe(AppSession session)
    {
        var project = session.Current;
        if (project is null)
        {
            return new
            {
                open = false,
                history = new { canUndo = false, canRedo = false, undoLabel = (string?)null,
                                redoLabel = (string?)null, depth = 0 },
            };
        }

        var document = project.Document;

        return new
        {
            open = true,
            dirty = session.Dirty,
            directory = project.Directory,
            packagePath = project.PackagePath,
            project = document,
            isMock = document.IsMock,
            activeAssetId = document.Active.Id,

            // Where each garment's files actually live, so the UI never has to
            // build a path itself.
            resolved = new
            {
                ydd = Resolve(project, document.BaseYddPath),
                ytd = Resolve(project, document.BaseYtdPath),
                ymt = Resolve(project, document.YmtTemplatePath),
            },

            assetPaths = document.Assets.ToDictionary(
                a => a.Id,
                a => (object)new
                {
                    ydd = Resolve(project, a.BaseYddPath),
                    ytd = Resolve(project, a.BaseYtdPath),
                }),

            history = new
            {
                canUndo = session.Commands.CanUndo,
                canRedo = session.Commands.CanRedo,
                undoLabel = session.Commands.UndoLabel,
                redoLabel = session.Commands.RedoLabel,
                depth = session.Commands.UndoCount,
                recent = session.Commands.RecentLabels(),
            },

            migration = project.OpenedFromSchema < ClothingProject.CurrentSchemaVersion
                ? new
                {
                    fromSchema = project.OpenedFromSchema,
                    toSchema = ClothingProject.CurrentSchemaVersion,
                    notes = project.MigrationNotes,
                }
                : null,
        };
    }

    /// <summary>
    /// What the export needs to make this garment glow, or null when it does
    /// not.
    /// </summary>
    /// <remarks>
    /// Refuses rather than falls back when the drawable is missing. Silently
    /// skipping would ship a garment that looks finished and stays dark in
    /// game, and the user would have no way to tell which of the two halves of
    /// the effect went missing.
    /// </remarks>
    private static EmissiveRequest? EmissiveFor(OpenProject project, ClothingAsset asset)
    {
        if (!asset.Emissive) return null;

        var ydd = Resolve(project, asset.BaseYddPath);
        if (ydd is null || !File.Exists(ydd))
        {
            throw new EditorException("emissive_no_drawable",
                $"'{asset.Name}' is set to glow, but its drawable is not in the project.",
                "A glowing garment ships its own mesh. Import the garment's .ydd, "
                + "or turn Neon off.");
        }

        return new EmissiveRequest(ydd, asset.EmissiveMultiplier);
    }

    private static string? Resolve(OpenProject project, string? relative) =>
        string.IsNullOrEmpty(relative) ? null : ProjectService.ResolveInProject(project, relative);

    /// <summary>
    /// Import a garment's diffuse texture, and with it every other colour slot
    /// of the same garment lying beside it. Returns the imported path of the
    /// chosen file.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A re-skin writes each colour into that slot's own game file, because the
    /// slots are not interchangeable: drawable 0's 'a' is BC3 at 349520 bytes
    /// where its 'b' is BC1 at 174760. So exporting a second colour needs
    /// <c>jbib_diff_000_b_uni.ytd</c> itself, and the export looks for it in the
    /// project's own <c>assets/</c> folder.
    /// </para>
    /// <para>
    /// Nothing later in the pipeline can find that file. Searching the wider
    /// asset library would be wrong, not merely slow: <c>jbib_diff_000_a_uni.ytd</c>
    /// exists in the base ped folder and again inside beach DLC, and they are
    /// different garments. The one moment we know which folder is the right one
    /// is here, when the user points at a file inside it.
    /// </para>
    /// <para>
    /// Failures are swallowed on purpose. A sibling that will not copy costs
    /// that one extra colour, and the export says so plainly when it is reached;
    /// it must not fail the import of the file the user actually chose.
    /// </para>
    /// </remarks>
    private static string ImportBaseYtd(AppSession session, OpenProject project, string ytd)
    {
        var imported = ImportSlotFile(project, ytd);

        var name = Path.GetFileNameWithoutExtension(ytd);
        var folder = Path.GetDirectoryName(Path.GetFullPath(ytd));
        if (folder is null
            || !ClothingNames.TryParseDiffuseTexture(
                name, out var component, out var drawable, out var chosen, out var race))
            return imported;

        var extension = Path.GetExtension(ytd);
        var copied = 0;

        for (var slot = 0; slot < 26; slot++)
        {
            var letter = ClothingNames.VariantLetter(slot);
            if (letter == chosen) continue;

            var sibling = Path.Combine(
                folder,
                ClothingNames.DiffuseTexture(component, drawable, letter, race) + extension);
            if (!File.Exists(sibling)) continue;

            try
            {
                ImportSlotFile(project, sibling);
                copied++;
            }
            catch (Exception ex)
            {
                session.Log.Warn($"Could not import colour slot '{letter}' of {name}: {ex.Message}");
            }
        }

        if (copied > 0)
            session.Log.Info($"Imported {copied} further colour slot(s) beside {name}.");

        return imported;
    }

    /// <summary>
    /// Copy a game slot file into <c>assets/</c> under its own name, replacing
    /// any file already there.
    /// </summary>
    /// <remarks>
    /// <see cref="ProjectService.ImportInto"/> renames on a name clash, which is
    /// right for a user's own layers and wrong here: the export finds a slot by
    /// its canonical name (<c>jbib_diff_000_b_uni.ytd</c>), so a file parked
    /// beside it as "(2)" is invisible and the stale original wins. Two files
    /// sharing a slot name are the same slot; the one the user just pointed at
    /// is the one they meant.
    /// </remarks>
    private static string ImportSlotFile(OpenProject project, string source)
    {
        var directory = Path.Combine(project.Directory, "assets");
        Directory.CreateDirectory(directory);

        var destination = Path.Combine(directory, Path.GetFileName(source));
        if (!string.Equals(Path.GetFullPath(source), Path.GetFullPath(destination),
                StringComparison.OrdinalIgnoreCase))
            File.Copy(source, destination, overwrite: true);

        return Path.GetRelativePath(project.Directory, destination).Replace('\\', '/');
    }

    private static DateTimeOffset? SafeModified(string path)
    {
        try
        {
            if (File.Exists(path)) return new FileInfo(path).LastWriteTime;
            if (Directory.Exists(path)) return new DirectoryInfo(path).LastWriteTime;
        }
        catch { /* unreadable */ }
        return null;
    }

    // ------------------------------------------------------------------
    // library
    // ------------------------------------------------------------------

    private static void RegisterLibrary(
        HostBridge bridge, AppSession session, Func<System.Windows.Window?> window)
    {
        bridge.On("library.scan", args =>
        {
            var root = HostBridge.OptionalString(args, "root")
                       ?? session.Settings.Current.AssetLibraryPath;
            var result = session.Library.Scan(root);
            return new
            {
                root = result.Root,
                note = result.Note,
                scanned = result.Scanned,
                assets = result.Assets.Select(a => new
                {
                    id = a.Id,
                    name = a.Name,
                    path = a.Path,
                    male = a.Male,
                    component = a.Component is null ? (int?)null : (int)a.Component,
                    componentPrefix = a.Component is null ? null : ClothingNames.Prefix(a.Component.Value),
                    drawableIndex = a.DrawableIndex,
                    sizeBytes = a.SizeBytes,
                    modified = a.Modified,
                    textureCount = a.TexturePaths.Count,
                    texturePaths = a.TexturePaths,
                    isMock = a.IsMock,
                }),
            };
        });

        bridge.On("library.pickRoot", _ =>
        {
            var dialog = new OpenFolderDialog { Title = "Choose the asset library folder" };
            if (dialog.ShowDialog(window()) != true) return null;
            session.Settings.Update(s => s.AssetLibraryPath = dialog.FolderName);
            return new { root = dialog.FolderName };
        });

        // ------------------------------------------------------------------
        // thumbnails
        // ------------------------------------------------------------------

        // Renders the real drawable if this is the first request, then serves
        // it from cache. A card that cannot be rendered gets a reason, never a
        // blank square passed off as a preview.
        bridge.On("library.thumbnail", async args =>
        {
            var path = HostBridge.RequiredString(args, "path");
            if (!File.Exists(path))
                throw new EditorException("not_found", "That drawable no longer exists.");

            var result = await session.Thumbnails.GetAsync(path, async ct =>
            {
                var backend = await session.BackendAsync(ct);
                var blobPath = Paths.NewTempFile(".mesh");
                try
                {
                    // The low LOD is plenty at 256px and is markedly cheaper to
                    // extract than the high one.
                    var mesh = await backend.ExtractMeshAsync(path, blobPath, "low", ct);
                    return (mesh, await File.ReadAllBytesAsync(blobPath, ct));
                }
                finally
                {
                    try { if (File.Exists(blobPath)) File.Delete(blobPath); } catch { /* temp */ }
                }
            });

            if (result.State != "ready" || result.Path is null)
                return new { state = result.State, reason = result.Reason, dataUrl = (string?)null };

            return new
            {
                state = "ready",
                reason = (string?)null,
                dataUrl = "data:image/png;base64,"
                          + Convert.ToBase64String(await File.ReadAllBytesAsync(result.Path)),
            };
        });

        bridge.On("library.thumbnailPeek", args =>
        {
            var path = HostBridge.RequiredString(args, "path");
            var result = session.Thumbnails.Peek(path);
            return new { state = result.State, reason = result.Reason };
        });

        bridge.On("cache.usage", _ =>
        {
            var (files, bytes) = session.Thumbnails.Usage();
            return new { thumbnails = new { files, bytes } };
        });

        bridge.On("cache.clear", _ =>
        {
            var (files, bytes) = session.Thumbnails.Clear();
            return new { files, bytes };
        });

        // ------------------------------------------------------------------
        // global search (Ctrl+P)
        // ------------------------------------------------------------------

        bridge.On("search.query", args =>
        {
            var needle = (HostBridge.OptionalString(args, "q") ?? "").Trim();
            var limit = Math.Clamp(HostBridge.OptionalInt(args, "limit", 40), 1, 200);
            if (needle.Length == 0) return Array.Empty<object>();

            var results = new List<object>();

            bool Matches(string? text) =>
                text is not null && text.Contains(needle, StringComparison.OrdinalIgnoreCase);

            // Library drawables
            foreach (var asset in session.Library.Scan(session.Settings.Current.AssetLibraryPath).Assets)
            {
                if (results.Count >= limit) break;
                if (!Matches(asset.Name) && !Matches(asset.Path)) continue;

                results.Add(new
                {
                    kind = "asset",
                    label = asset.Name,
                    detail = asset.Component is null
                        ? asset.Path
                        : $"{ClothingNames.Label(asset.Component.Value)} - {asset.Path}",
                    path = asset.Path,
                });
            }

            // Recent projects
            foreach (var recent in session.Settings.Current.RecentProjects)
            {
                if (results.Count >= limit) break;
                if (!Matches(Path.GetFileNameWithoutExtension(recent)) && !Matches(recent)) continue;

                results.Add(new
                {
                    kind = "project",
                    label = Path.GetFileNameWithoutExtension(recent),
                    detail = recent,
                    path = recent,
                });
            }

            // Anything inside the open project
            if (session.Current is { } open)
            {
                foreach (var asset in open.Document.Assets)
                {
                    if (results.Count >= limit) break;
                    if (Matches(asset.Name))
                    {
                        results.Add(new
                        {
                            kind = "garment",
                            label = asset.Name,
                            detail = $"{ClothingNames.Label(asset.Component)} "
                                     + $"- drawable {asset.DrawableIndex}",
                            path = (string?)asset.Id,
                        });
                    }

                    foreach (var variation in asset.Variations)
                    {
                        if (results.Count >= limit) break;
                        if (!Matches(variation.Name)) continue;

                        results.Add(new
                        {
                            kind = "variation",
                            label = variation.Name,
                            detail = $"{asset.Name} - variant {variation.Variant}",
                            path = (string?)variation.Id,
                        });
                    }
                }
            }

            return results;
        });
    }

    // ------------------------------------------------------------------
    // assets
    // ------------------------------------------------------------------

    private static void RegisterAsset(HostBridge bridge, AppSession session)
    {
        // Where each kind of Browse was last pointed. Seeded from the asset
        // library so the first pick lands where the game files actually are,
        // then follows the user rather than yanking them back every time.
        var lastPickDirectory = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        bridge.On("asset.inspect", async args =>
        {
            var path = HostBridge.RequiredString(args, "path");
            if (!File.Exists(path))
                throw new EditorException("not_found", $"File not found: {Path.GetFileName(path)}");

            var backend = await session.BackendAsync();
            return Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".ydd" => await backend.ReadYddAsync(path),
                ".ytd" => await backend.ReadYtdAsync(path),
                ".ymt" => (object)await backend.ReadYmtAsync(path),
                var ext => throw new EditorException("unsupported_format",
                    $"'{ext}' is not a GTA asset this application can read.",
                    "Supported: .ydd, .ytd, .ymt"),
            };
        });

        // Real geometry from a real drawable, or clearly-labelled synthetic
        // geometry when there is no asset to load.
        bridge.On("asset.mesh", async args =>
        {
            var path = HostBridge.OptionalString(args, "path");
            var lod = HostBridge.OptionalString(args, "lod") ?? "high";

            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                var mock = MockMeshFactory.Torso();
                return new
                {
                    isMock = true,
                    source = mock.Source,
                    vertexCount = mock.VertexCount,
                    triangleCount = mock.TriangleCount,
                    lod = "n/a",
                    positions = mock.Positions,
                    normals = mock.Normals,
                    uvs = mock.Uvs,
                    indices = mock.Indices,
                    materials = Array.Empty<object>(),
                };
            }

            var backend = await session.BackendAsync();
            var blob = Paths.NewTempFile(".mesh");
            try
            {
                var mesh = await backend.ExtractMeshAsync(path, blob, lod);
                var bytes = await File.ReadAllBytesAsync(blob);

                return new
                {
                    isMock = false,
                    source = Path.GetFileName(path),
                    mesh.VertexCount,
                    mesh.TriangleCount,
                    mesh.Lod,
                    mesh.Materials,
                    mesh.Bounds,
                    // One base64 blob rather than four JSON float arrays: at
                    // 15k vertices the arrays alone would be megabytes of text.
                    buffer = Convert.ToBase64String(bytes),
                    layout = mesh.ByteLayout,
                };
            }
            finally
            {
                try { if (File.Exists(blob)) File.Delete(blob); } catch { /* temp */ }
            }
        });

        // ------------------------------------------------------------------
        // developer mode: raw inspection
        // ------------------------------------------------------------------

        // Facts about the file itself, independent of whether a parser accepts
        // it. This is the first thing to look at when an export misbehaves.
        bridge.On("asset.stat", args =>
        {
            var path = HostBridge.RequiredString(args, "path");
            if (!File.Exists(path))
                throw new EditorException("not_found", $"File not found: {Path.GetFileName(path)}");

            var info = new FileInfo(path);
            var (hash, partial) = BinaryInspector.Hash(path);

            return new
            {
                path,
                name = info.Name,
                extension = info.Extension.TrimStart('.').ToLowerInvariant(),
                sizeBytes = info.Length,
                modified = (DateTimeOffset)info.LastWriteTimeUtc,
                magic = BinaryInspector.ReadMagic(path),
                sha256 = hash,
                sha256Partial = partial,
            };
        });

        bridge.On("asset.hex", args =>
        {
            RequireDeveloperMode(session);

            var window_ = BinaryInspector.Read(
                HostBridge.RequiredString(args, "path"),
                HostBridge.OptionalInt(args, "offset", 0),
                HostBridge.OptionalInt(args, "length", 1024));

            return new
            {
                path = window_.Path,
                fileSize = window_.FileSize,
                offset = window_.Offset,
                length = window_.Length,
                magic = window_.Magic,
                base64 = window_.Base64,
            };
        });

        bridge.On("asset.findBytes", args =>
        {
            RequireDeveloperMode(session);

            var path = HostBridge.RequiredString(args, "path");
            var pattern = HostBridge.OptionalString(args, "hex");
            var text = HostBridge.OptionalString(args, "text");

            byte[] needle;
            if (!string.IsNullOrEmpty(pattern))
            {
                var cleaned = pattern.Replace(" ", "").Replace("-", "");
                if (cleaned.Length == 0 || cleaned.Length % 2 != 0)
                    throw new EditorException("bad_args", "Hex must be an even number of digits.");
                try { needle = Convert.FromHexString(cleaned); }
                catch (FormatException)
                {
                    throw new EditorException("bad_args", "That is not valid hexadecimal.");
                }
            }
            else if (!string.IsNullOrEmpty(text))
            {
                needle = System.Text.Encoding.ASCII.GetBytes(text);
            }
            else
            {
                throw new EditorException("bad_args", "Supply either 'hex' or 'text' to search for.");
            }

            var offset = BinaryInspector.Find(path, needle, HostBridge.OptionalInt(args, "from", 0));
            return new { offset, found = offset >= 0 };
        });

        bridge.On("asset.pickFile", args =>
        {
            var kind = HostBridge.OptionalString(args, "kind") ?? "asset";
            var filter = kind switch
            {
                "ydd" => "Drawable dictionary (*.ydd)|*.ydd",
                "ytd" => "Texture dictionary (*.ytd)|*.ytd",
                "ymt" => "Ped metadata (*.ymt)|*.ymt",
                "image" => "Images (*.png;*.jpg;*.jpeg;*.webp;*.dds)|*.png;*.jpg;*.jpeg;*.webp;*.dds",
                _ => "GTA assets (*.ydd;*.ytd;*.ymt)|*.ydd;*.ytd;*.ymt|Images (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg",
            };

            var dialog = new OpenFileDialog
            {
                Title = "Import",
                Filter = filter + "|All files (*.*)|*.*",
                Multiselect = HostBridge.OptionalBool(args, "multiple", false),
            };

            // Read afresh so a library folder changed in Settings takes effect
            // without a restart. Never let this stop the dialog opening: a
            // picker that starts in the wrong folder is a nuisance, a picker
            // that throws is a dead button.
            try
            {
                var start = FilePickerStart.Resolve(
                    kind, lastPickDirectory,
                    session.Settings.Current.AssetLibraryPath, Directory.Exists);
                if (start is not null) dialog.InitialDirectory = start;
            }
            catch (Exception ex)
            {
                session.Log.Error("Could not choose a start folder for the file picker", ex);
            }

            if (dialog.ShowDialog() != true) return null;

            var directory = Path.GetDirectoryName(dialog.FileName);
            if (!string.IsNullOrEmpty(directory))
                lastPickDirectory[FilePickerStart.GroupKey(kind)] = directory;

            return new { paths = dialog.FileNames };
        });

        bridge.On("asset.importImage", args =>
        {
            var project = session.RequireProject();
            var source = HostBridge.RequiredString(args, "path");

            var ext = Path.GetExtension(source).ToLowerInvariant();
            string[] supported = { ".png", ".jpg", ".jpeg", ".bmp", ".webp" };
            if (!supported.Contains(ext))
            {
                throw new EditorException("unsupported_format",
                    $"'{ext}' images cannot be imported.",
                    "Supported: PNG, JPG, BMP, WEBP. DDS import is not implemented yet.");
            }

            var relative = ProjectService.ImportInto(project, source, "layers");
            session.Dirty = true;
            return new { relativePath = relative, fullPath = Resolve(project, relative) };
        });

        // Serves a project file to the WebView as a data URL. Only paths inside
        // the project root resolve, so a crafted project cannot read the disk.
        bridge.On("asset.readImage", args =>
        {
            var project = session.RequireProject();
            var relative = HostBridge.RequiredString(args, "relativePath");
            var full = ProjectService.ResolveInProject(project, relative);
            if (!File.Exists(full))
                throw new EditorException("not_found", "That image is no longer in the project folder.");

            var mime = Path.GetExtension(full).ToLowerInvariant() switch
            {
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".bmp" => "image/bmp",
                ".webp" => "image/webp",
                _ => "application/octet-stream",
            };
            return new { dataUrl = $"data:{mime};base64,{Convert.ToBase64String(File.ReadAllBytes(full))}" };
        });

        // The garment's existing diffuse, decoded to PNG so the texture editor
        // can paint on top of it rather than on a blank canvas.
        bridge.On("texture.baseImage", async _ =>
        {
            var project = session.RequireProject();
            var ytd = Resolve(project, project.Document.BaseYtdPath);
            if (ytd is null || !File.Exists(ytd))
                return new { available = false, reason = "This project has no base texture dictionary." };

            var cacheKey = $"base_{project.Document.Variations.Count}_{Path.GetFileNameWithoutExtension(ytd)}.png";
            var png = Path.Combine(project.Directory, "previews", cacheKey);

            if (!File.Exists(png))
            {
                var backend = await session.BackendAsync();
                var dds = Paths.NewTempFile(".dds");
                try
                {
                    await backend.DecodeTextureAsync(ytd, dds);
                    Bitirim.Clothing.Textures.DdsImageDecoder.DdsToPng(dds, png);
                    session.Log.Info($"Decoded base texture for editing: {Path.GetFileName(ytd)}");
                }
                catch (Exception ex)
                {
                    session.Log.Error($"Base texture decode failed for {ytd}", ex);
                    return new { available = false, reason = "The base texture could not be decoded." };
                }
                finally
                {
                    try { if (File.Exists(dds)) File.Delete(dds); } catch { /* temp */ }
                }
            }

            return new
            {
                available = true,
                reason = (string?)null,
                dataUrl = $"data:image/png;base64,{Convert.ToBase64String(File.ReadAllBytes(png))}",
            };
        });

        // Writes a composited texture from the UI back into the project.
        bridge.On("texture.saveComposite", args =>
        {
            var project = session.RequireProject();
            var variationId = HostBridge.RequiredString(args, "variationId");
            var dataUrl = HostBridge.RequiredString(args, "dataUrl");

            var comma = dataUrl.IndexOf(',');
            if (comma < 0) throw new EditorException("bad_args", "The image data was malformed.");

            var bytes = Convert.FromBase64String(dataUrl[(comma + 1)..]);
            var dir = Path.Combine(project.Directory, "textures");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, $"{variationId}.png");
            File.WriteAllBytes(file, bytes);

            var variation = project.Document.Variations.FirstOrDefault(v => v.Id == variationId);
            if (variation is not null)
                variation.TexturePath = Path.GetRelativePath(project.Directory, file).Replace('\\', '/');

            // The glow mask is a second image, not this one's alpha channel:
            // the design also feeds the shop thumbnail, and an alpha that
            // means "glows here" would render as a hole on the shelf.
            var glowUrl = HostBridge.OptionalString(args, "glowDataUrl");
            long glowBytes = 0;
            var glowFile = Path.Combine(dir, $"{variationId}.glow.png");

            if (!string.IsNullOrEmpty(glowUrl))
            {
                var mark = glowUrl.IndexOf(',');
                if (mark < 0)
                    throw new EditorException("bad_args", "The glow mask data was malformed.");

                var mask = Convert.FromBase64String(glowUrl[(mark + 1)..]);
                File.WriteAllBytes(glowFile, mask);
                glowBytes = mask.Length;
                if (variation is not null)
                    variation.GlowPath =
                        Path.GetRelativePath(project.Directory, glowFile).Replace('\\', '/');
            }
            else if (variation is not null)
            {
                // Every glow layer was removed. Clearing the path is not
                // enough on its own -- a stale file on disk would keep
                // exporting a mask the project no longer has.
                variation.GlowPath = null;
                if (File.Exists(glowFile)) File.Delete(glowFile);
            }

            session.Dirty = true;
            return new
            {
                path = variation?.TexturePath,
                sizeBytes = bytes.Length,
                glowPath = variation?.GlowPath,
                glowSizeBytes = glowBytes,
            };
        });
    }

    // ------------------------------------------------------------------
    // validation + export
    // ------------------------------------------------------------------

    private static void RegisterExport(
        HostBridge bridge, AppSession session, Func<System.Windows.Window?> window)
    {
        // ------------------------------------------------------------------
        // validation
        // ------------------------------------------------------------------

        bridge.On("validate.run", async _ =>
        {
            var project = session.RequireProject();
            var document = project.Document;
            var capabilities = await session.TryCapabilitiesAsync();

            // Read every garment once, so the validator works from what the
            // files actually contain rather than from the document's claims.
            var evidence = new List<Editor.Validation.AssetEvidence>();

            foreach (var asset in document.Assets)
            {
                var ydd = Resolve(project, asset.BaseYddPath);
                var ytd = Resolve(project, asset.BaseYtdPath);

                Core.Rage.DrawableDictionaryInfo? drawable = null;
                Core.Rage.TextureDictionaryInfo? texture = null;
                string? failure = null;

                if (!asset.IsMock && ydd is not null && File.Exists(ydd))
                {
                    try
                    {
                        var backend = await session.BackendAsync();
                        drawable = await backend.ReadYddAsync(ydd);
                        if (ytd is not null && File.Exists(ytd))
                            texture = await backend.ReadYtdAsync(ytd);
                    }
                    catch (EditorException ex)
                    {
                        failure = ex.Message;
                    }
                }

                evidence.Add(new Editor.Validation.AssetEvidence(
                    asset, ydd, ytd, drawable, texture, failure));
            }

            var report = session.ProjectValidator.Validate(document, evidence, capabilities);
            session.Log.Info($"Validation: {report.Status} ({report.Findings.Count} finding(s))");

            return new
            {
                status = report.Status,
                categories = FindingCategories.All,
                counts = new
                {
                    error = report.Findings.Count(f => f.Severity == Severity.Error),
                    warning = report.Findings.Count(f => f.Severity == Severity.Warning),
                    info = report.Findings.Count(f => f.Severity == Severity.Info),
                },
                findings = report.Findings.Select(f => new
                {
                    severity = f.Severity.ToString().ToLowerInvariant(),
                    category = f.Category,
                    code = f.Code,
                    message = f.Message,
                    hint = f.Hint,
                    target = f.Target,
                }),
            };
        });

        // ------------------------------------------------------------------
        // export
        // ------------------------------------------------------------------

        bridge.On("export.presets", _ => ExportPresets.All.Select(p => new
        {
            id = p.Id,
            name = p.Name,
            description = p.Description,
            kind = p.Kind.ToString(),
            requiresRealAsset = p.RequiresRealAsset,
            experimental = p.Experimental,
        }));

        bridge.On("export.history", _ =>
        {
            var project = session.RequireProject();
            return project.Document.Export.History.Select(h => new
            {
                at = h.At,
                resourceName = h.ResourceName,
                directory = h.Directory,
                preset = h.Preset,
                fileCount = h.FileCount,
                totalBytes = h.TotalBytes,
                status = h.Status,
                exists = Directory.Exists(h.Directory),
            });
        });

        bridge.On("export.pickFolder", _ =>
        {
            var dialog = new OpenFolderDialog { Title = "Choose the export folder" };
            if (dialog.ShowDialog(window()) != true) return null;
            return new { path = dialog.FolderName };
        });

        bridge.On("export.cancel", _ =>
        {
            var running = session.RunningExport;
            if (running is null) return new { cancelled = false };
            running.Cancel();
            session.Log.Warn("Export cancelled by the user.");
            return new { cancelled = true };
        });

        // Reports what an export would write, without writing anything. This is
        // what the wizard's review step shows: names are derived by exactly the
        // same helpers that will produce the files.
        bridge.On("export.plan", args =>
        {
            var project = session.RequireProject();
            var document = project.Document;
            var preset = ExportPresets.Resolve(
                HostBridge.OptionalString(args, "preset") ?? document.Export.Preset);

            var chosen = SelectedAssets(document, args);
            var multi = chosen.Count > 1;
            var replace = string.Equals(
                document.Export.Mode, "replace", StringComparison.OrdinalIgnoreCase);

            var files = new List<object>();
            foreach (var asset in chosen)
            {
                // Re-skinning writes texture dictionaries and nothing else: the
                // mesh and the ped metadata stay the game's own. Listing a .ydd
                // or .ymt here would promise files the export will not write.
                if (replace)
                {
                    foreach (var variation in asset.Variations.OrderBy(v => v.Index))
                    {
                        files.Add(new
                        {
                            garment = asset.Name,
                            kind = "ytd",
                            name = ReplaceResourceBuilder.StreamFileName(
                                document.Ped, document.Export.HostDlc,
                                ClothingNames.DiffuseTexture(
                                    asset.Component, asset.DrawableIndex, variation.Variant))
                                + ".ytd",
                        });
                    }
                    continue;
                }

                var dlc = multi
                    ? $"{document.Export.DlcName}_{ClothingNames.Prefix(asset.Component)}_{asset.DrawableIndex:D3}"
                    : document.Export.DlcName;

                var drawableAsset = ClothingNames.Drawable(asset.Component, 0);
                files.Add(new
                {
                    garment = asset.Name,
                    kind = "ydd",
                    name = ClothingNames.StreamFile(document.Ped, dlc, drawableAsset, "ydd"),
                });

                foreach (var variation in asset.Variations.OrderBy(v => v.Index))
                {
                    files.Add(new
                    {
                        garment = asset.Name,
                        kind = "ytd",
                        name = ClothingNames.StreamFile(document.Ped, dlc,
                            ClothingNames.DiffuseTexture(asset.Component, 0, variation.Variant), "ytd"),
                    });
                }

                files.Add(new
                {
                    garment = asset.Name,
                    kind = "ymt",
                    name = ClothingNames.MetadataFile(document.Ped, dlc),
                });
            }

            return new
            {
                preset = preset.Id,
                experimental = preset.Experimental,
                resourceName = document.Export.ResourceName,
                ped = document.Ped,
                garments = chosen.Select(a => new
                {
                    id = a.Id,
                    name = a.Name,
                    component = (int)a.Component,
                    componentPrefix = ClothingNames.Prefix(a.Component),
                    drawableIndex = a.DrawableIndex,
                    variations = a.Variations.Count,
                    unsavedVariations = a.Variations.Count(v => string.IsNullOrEmpty(v.TexturePath)),
                    isMock = a.IsMock,
                }),
                files,
            };
        });

        bridge.On("export.run", async args =>
        {
            var project = session.RequireProject();
            var document = project.Document;
            var preset = ExportPresets.Resolve(
                HostBridge.OptionalString(args, "preset") ?? document.Export.Preset);

            var outputDir = HostBridge.OptionalString(args, "output")
                            ?? document.Export.LastOutputDirectory
                            ?? session.Settings.Current.DefaultExportFolder
                            ?? Path.Combine(project.Directory, "exports");
            Directory.CreateDirectory(outputDir);

            using var cancellation = new CancellationTokenSource();
            session.RunningExport = cancellation;

            try
            {
                var result = preset.Kind switch
                {
                    ExportKind.TexturePack =>
                        await ExportTexturePackAsync(bridge, session, project, outputDir, args,
                            cancellation.Token),

                    ExportKind.ProjectArchive =>
                        ExportProjectArchive(session, project, outputDir),

                    _ => await ExportResourceAsync(bridge, session, project, preset, outputDir, args,
                            cancellation.Token),
                };

                RecordExport(session, project, preset, result);
                return result;
            }
            catch (OperationCanceledException)
            {
                await bridge.EmitAsync("export.progress",
                    new { stage = "Cancelled", percent = 0, cancelled = true });
                throw new EditorException("export_cancelled", "The export was cancelled.",
                    "Files already written were left in place.");
            }
            finally
            {
                session.RunningExport = null;
            }
        });
    }

    /// <summary>The garments an export run should include.</summary>
    private static List<ClothingAsset> SelectedAssets(ClothingProject document, JsonElement args)
    {
        var requested = args.ValueKind == JsonValueKind.Object
                        && args.TryGetProperty("assetIds", out var element)
                        && element.ValueKind == JsonValueKind.Array
            ? element.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString()!)
                .ToHashSet(StringComparer.Ordinal)
            : null;

        var chosen = document.Assets
            .Where(a => requested is null ? a.IncludeInExport : requested.Contains(a.Id))
            .ToList();

        return chosen.Count > 0 ? chosen : new List<ClothingAsset> { document.Active };
    }

    /// <summary>
    /// Writes a FiveM addon clothing resource.
    /// </summary>
    /// <remarks>
    /// Every file here is real RAGE output, re-parsed after writing. It has
    /// still never been loaded by a running FiveM client, which is why the
    /// result carries <c>experimental: true</c> and the UI says so.
    /// </remarks>
    private static async Task<object> ExportResourceAsync(
        HostBridge bridge,
        AppSession session,
        OpenProject project,
        ExportPreset preset,
        string outputDir,
        JsonElement args,
        CancellationToken ct)
    {
        var document = project.Document;
        var chosen = SelectedAssets(document, args);

        var mock = chosen.Where(a => a.IsMock).ToList();
        if (mock.Count > 0)
        {
            throw new EditorException("mock_export",
                mock.Count == chosen.Count
                    ? "A mock project cannot be exported as a FiveM resource."
                    : $"{mock.Count} of the selected garments use mock assets and cannot be exported.",
                "Mock assets exist so the editor can be used without game files. "
                + "Import a real .ydd drawable to export.");
        }

        if (string.Equals(document.Export.Mode, "replace", StringComparison.OrdinalIgnoreCase))
        {
            return await ExportReplaceAsync(
                bridge, session, project, preset, outputDir, chosen, ct);
        }

        var ymt = Resolve(project, document.YmtTemplatePath)
                  ?? throw new EditorException("no_ymt", "This project has no ped metadata template.");

        await bridge.EmitAsync("export.progress", new { stage = "Encoding textures", percent = 6 });

        var garments = new List<AddonGarment>();
        var encoded = 0;

        foreach (var asset in chosen)
        {
            ct.ThrowIfCancellationRequested();

            var ydd = Resolve(project, asset.BaseYddPath)
                      ?? throw new EditorException("no_ydd",
                          $"'{asset.Name}' has no base drawable to export.");
            var ytd = Resolve(project, asset.BaseYtdPath)
                      ?? throw new EditorException("no_ytd",
                          $"'{asset.Name}' has no base texture dictionary.");

            var slots = new List<TextureSlot>();
            foreach (var variation in asset.Variations.OrderBy(v => v.Index))
            {
                ct.ThrowIfCancellationRequested();

                var texture = variation.TexturePath is null
                    ? null
                    : Resolve(project, variation.TexturePath);

                if (texture is null || !File.Exists(texture))
                {
                    throw new EditorException("variation_empty",
                        $"'{asset.Name}' variation '{variation.Name}' has no saved texture yet.",
                        "Open the Texture tab and save the variation before exporting.");
                }

                var bytes = session.Encoder.EncodeFile(texture, document.Export.TextureFormat);
                slots.Add(new TextureSlot(variation.Variant, texture, bytes));

                encoded++;
                await bridge.EmitAsync("export.progress", new
                {
                    stage = $"Encoding {asset.Name} / {variation.Name}",
                    percent = Math.Min(40, 6 + encoded * 4),
                });
            }

            garments.Add(new AddonGarment(
                asset.Name, asset.Component, asset.DrawableIndex, ydd, ytd, slots));
        }

        var backend = await session.BackendAsync(ct);
        var builder = new AddonResourceBuilder(backend, session.Validator);

        var progress = new Progress<(string Stage, int Percent)>(update =>
            _ = bridge.EmitAsync("export.progress",
                new { stage = update.Stage, percent = update.Percent }));

        var request = new MultiAddonExportRequest(
            ResourceName: document.Export.ResourceName,
            Ped: document.Ped,
            PackDlcName: document.Export.DlcName,
            YmtTemplatePath: ymt,
            Garments: garments,
            OutputDirectory: outputDir,
            ManifestMode: Enum.TryParse<ManifestMode>(document.Export.ManifestMode, true, out var mode)
                ? mode
                : ManifestMode.Stream);

        var result = await builder.BuildManyAsync(
            request, suffixDlcNames: garments.Count > 1, progress, ct);

        document.Export.LastOutputDirectory = outputDir;
        document.Export.Preset = preset.Id;

        var files = result.Files.Select(f => new
        {
            name = Path.GetFileName(f),
            path = f,
            sizeBytes = new FileInfo(f).Length,
        }).ToList();

        // A development package adds the project archive and the written report
        // beside the resource, so a handover is one folder.
        if (preset.Kind == ExportKind.DevelopmentPackage)
        {
            var archive = Path.Combine(result.ResourceDirectory,
                document.Name + ProjectService.PackageExtension);
            session.Projects.SaveAsPackage(project, archive);

            var report = Path.Combine(result.ResourceDirectory, "validation-report.md");
            await File.WriteAllTextAsync(report, BuildValidationReport(document, result), ct);

            files.Add(new
            {
                name = Path.GetFileName(archive),
                path = archive,
                sizeBytes = new FileInfo(archive).Length,
            });
            files.Add(new
            {
                name = Path.GetFileName(report),
                path = report,
                sizeBytes = new FileInfo(report).Length,
            });
        }

        session.Projects.Save(project);
        session.Log.Export($"Exported {document.Export.ResourceName} to {result.ResourceDirectory} "
                           + $"({files.Count} files, {result.Validation.Status})");

        return new
        {
            preset = preset.Id,
            kind = preset.Kind.ToString(),
            experimental = preset.Experimental,
            directory = result.ResourceDirectory,
            garments = garments.Count,
            files,
            status = result.Validation.Status,
            readBackOk = result.ReadBack.AllOk,
            warnings = result.Validation.Findings.Count(f => f.Severity == Severity.Warning),
            errors = result.Validation.Findings.Count(f => f.Severity == Severity.Error),
            findings = result.Validation.Findings.Select(f => new
            {
                severity = f.Severity.ToString().ToLowerInvariant(),
                category = f.Category,
                code = f.Code,
                message = f.Message,
                hint = f.Hint,
                target = f.Target,
            }),
            timings = result.Timings,
        };
    }

    /// <summary>
    /// Writes a resource that re-skins garments the game already ships.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Unlike the addon route this one has been seen working in a running
    /// client: ten colours applied to a base-ped jacket, no mesh shipped and no
    /// metadata of any kind. It is also the shape every published clothing pack
    /// examined here uses.
    /// </para>
    /// <para>
    /// Each colour is written into the game's own file for that exact slot, so
    /// the sources are the siblings of the asset's base texture -- slot 'c'
    /// needs <c>..._c_uni.ytd</c>, not the base 'a'. That is a derivation, and
    /// it is checked: a missing sibling is reported by name rather than
    /// silently substituted, because substituting would mean rebuilding the
    /// container, which crashes the graphics driver on load.
    /// </para>
    /// </remarks>
    private static async Task<object> ExportReplaceAsync(
        HostBridge bridge,
        AppSession session,
        OpenProject project,
        ExportPreset preset,
        string outputDir,
        List<ClothingAsset> chosen,
        CancellationToken ct)
    {
        var document = project.Document;
        var backend = await session.BackendAsync(ct);
        var builder = new ReplaceResourceBuilder(backend);

        var files = new List<object>();
        var issues = new List<string>();
        var directory = "";
        var encoded = 0;

        await bridge.EmitAsync("export.progress", new { stage = "Encoding textures", percent = 6 });

        foreach (var asset in chosen)
        {
            ct.ThrowIfCancellationRequested();

            var baseYtd = Resolve(project, asset.BaseYtdPath)
                          ?? throw new EditorException("no_ytd",
                              $"'{asset.Name}' has no base texture dictionary.");
            var libraryDirectory = Path.GetDirectoryName(baseYtd)!;

            var slots = new List<ReplaceSlot>();
            var designs = new List<string>();
            foreach (var variation in asset.Variations.OrderBy(v => v.Index))
            {
                ct.ThrowIfCancellationRequested();

                var texture = variation.TexturePath is null
                    ? null
                    : Resolve(project, variation.TexturePath);

                if (texture is null || !File.Exists(texture))
                {
                    throw new EditorException("variation_empty",
                        $"'{asset.Name}' variation '{variation.Name}' has no saved texture yet.",
                        "Open the Texture tab and save the variation before exporting.");
                }

                var slotName = ClothingNames.DiffuseTexture(
                    asset.Component, asset.DrawableIndex, variation.Variant);
                var slotSource = Path.Combine(libraryDirectory, slotName + ".ytd");
                if (!File.Exists(slotSource))
                {
                    throw new EditorException("slot_source_missing",
                        $"The game's own file for slot '{variation.Variant}' is not in the library: "
                        + $"{slotName}.ytd",
                        "Each colour is written into that slot's own file. Extract it next to "
                        + Path.GetFileName(baseYtd) + " and export again.");
                }

                // The format is the slot's, not the project's. Re-skinning
                // writes into the game's own container, so a colour has to come
                // out in whatever that slot already holds -- drawable 3's slots
                // are BC1 where drawable 0's 'a' is BC3. Honouring the project
                // setting here would just fail the shape check with a puzzling
                // message about a format the user never chose per slot.
                var slotFormat = (await backend.ReadYtdAsync(slotSource, ct))
                    .Textures[0].Format;

                // A glowing garment reads its brightness from the diffuse
                // alpha, so the mask is folded in here rather than earlier:
                // the design PNG stays untouched for the shop thumbnail, and
                // only the bytes going into the game carry the mask.
                var encodeFrom = texture;
                if (asset.Emissive)
                {
                    var mask = variation.GlowPath is null
                        ? null
                        : Resolve(project, variation.GlowPath);
                    encodeFrom = GlowMask.Apply(
                        texture, mask is not null && File.Exists(mask) ? mask : null,
                        Path.Combine(Path.GetTempPath(),
                            $"bcc-glow-{asset.Id}-{variation.Index}.png"));
                }

                var bytes = session.Encoder.EncodeFile(encodeFrom, slotFormat);
                slots.Add(new ReplaceSlot(variation.Variant, slotSource, bytes));
                designs.Add(texture);

                encoded++;
                await bridge.EmitAsync("export.progress", new
                {
                    stage = $"Encoding {asset.Name} / {variation.Name}",
                    percent = Math.Min(70, 6 + encoded * 4),
                });
            }

            var result = await builder.BuildAsync(new ReplaceExportRequest(
                ResourceName: document.Export.ResourceName,
                Ped: document.Ped,
                HostDlc: document.Export.HostDlc,
                Component: asset.Component,
                DrawableIndex: asset.DrawableIndex,
                Slots: slots,
                OutputDirectory: outputDir,
                ArmsDrawable: document.Export.ArmsDrawable,
                Emissive: EmissiveFor(project, asset)), ct);

            directory = result.ResourceDirectory;
            issues.AddRange(result.Issues);
            files.AddRange(result.Files.Select(f => new
            {
                name = Path.GetFileName(f),
                path = f,
                sizeBytes = new FileInfo(f).Length,
            }));

            // The shop shows prepared PNGs, not live renders, so a re-skinned
            // garment keeps advertising its old colours until these are redrawn.
            await bridge.EmitAsync("export.progress",
                new { stage = $"Drawing shop images for {asset.Name}", percent = 85 });

            var shopImages = await WriteShopImagesAsync(
                session, project, asset, result.ResourceDirectory, designs, ct);

            if (shopImages.SkippedBecause is { } reason)
            {
                issues.Add($"Shop images not drawn: {reason}");
            }

            files.AddRange(shopImages.Files.Select(f => new
            {
                name = Path.GetFileName(f),
                path = f,
                sizeBytes = new FileInfo(f).Length,
            }));
        }

        document.Export.LastOutputDirectory = outputDir;
        document.Export.Preset = preset.Id;
        session.Projects.Save(project);
        session.Log.Export($"Re-skin export: {document.Export.ResourceName} -> {directory} "
                           + $"({files.Count} files)");

        await bridge.EmitAsync("export.progress", new { stage = "Done", percent = 100 });

        return new
        {
            preset = preset.Id,
            kind = preset.Kind.ToString(),
            mode = "replace",
            experimental = preset.Experimental,
            directory,
            garments = chosen.Count,
            files,
            status = issues.Count == 0 ? "GREEN" : "AMBER",
            readBackOk = true,
            warnings = issues.Count,
            errors = 0,
            findings = issues.Select(i => new
            {
                severity = "warning",
                category = FindingCategories.Texture,
                code = "replace.issue",
                message = i,
                hint = (string?)null,
                target = (string?)null,
            }),
            timings = new Dictionary<string, double>(),
        };
    }

    /// <summary>
    /// Draws the clothing shop's card and swatches for a re-skinned garment.
    /// </summary>
    /// <remarks>
    /// Never fatal. A missing drawable or an unreadable design costs the shop
    /// its picture, not the export its files -- the garment still works in game
    /// without them, so failing the whole run here would be the wrong trade.
    /// The reason travels back as a warning instead.
    /// </remarks>
    private static async Task<ShopImageResult> WriteShopImagesAsync(
        AppSession session,
        OpenProject project,
        ClothingAsset asset,
        string resourceDirectory,
        IReadOnlyList<string> designs,
        CancellationToken ct)
    {
        var ydd = Resolve(project, asset.BaseYddPath);
        if (ydd is null || !File.Exists(ydd))
            return new ShopImageResult(Array.Empty<string>(), "the garment has no drawable.");

        var blobPath = Path.Combine(Path.GetTempPath(), $"bcc_shopmesh_{Guid.NewGuid():N}.bin");
        try
        {
            var backend = await session.BackendAsync(ct);
            var mesh = await backend.ExtractMeshAsync(ydd, blobPath, "high", ct);
            var blob = await File.ReadAllBytesAsync(mesh.Blob, ct);

            return ShopImageWriter.Write(new ShopImageRequest(
                resourceDirectory, asset.Component, asset.DrawableIndex,
                blob, mesh.ByteLayout, designs));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            session.Log.Error("Could not draw the shop images", ex);
            return new ShopImageResult(Array.Empty<string>(), ex.Message);
        }
        finally
        {
            try { if (File.Exists(blobPath)) File.Delete(blobPath); } catch { /* temp */ }
        }
    }

    /// <summary>
    /// Writes the composited textures as PNG.
    /// </summary>
    /// <remarks>
    /// Ordinary image files, not game assets: nothing here is experimental and
    /// the result says so, because labelling a PNG "unverified in FiveM" would
    /// be noise that devalues the warning where it matters.
    /// </remarks>
    private static async Task<object> ExportTexturePackAsync(
        HostBridge bridge,
        AppSession session,
        OpenProject project,
        string outputDir,
        JsonElement args,
        CancellationToken ct)
    {
        var document = project.Document;
        var chosen = SelectedAssets(document, args);

        var packDir = Path.Combine(outputDir, document.Export.ResourceName + "_textures");
        Directory.CreateDirectory(packDir);

        var files = new List<object>();
        var missing = 0;

        foreach (var asset in chosen)
        {
            foreach (var variation in asset.Variations.OrderBy(v => v.Index))
            {
                ct.ThrowIfCancellationRequested();

                var source = variation.TexturePath is null
                    ? null
                    : Resolve(project, variation.TexturePath);

                if (source is null || !File.Exists(source)) { missing++; continue; }

                var name = ClothingNames.DiffuseTexture(
                    asset.Component, asset.DrawableIndex, variation.Variant) + ".png";
                var destination = Path.Combine(packDir, name);
                File.Copy(source, destination, overwrite: true);

                files.Add(new
                {
                    name,
                    path = destination,
                    sizeBytes = new FileInfo(destination).Length,
                });
            }
        }

        if (files.Count == 0)
        {
            throw new EditorException("variation_empty",
                "No variation has a saved texture yet.",
                "Open the Texture tab and save at least one variation.");
        }

        await bridge.EmitAsync("export.progress", new { stage = "Done", percent = 100 });
        session.Log.Export($"Exported {files.Count} texture(s) to {packDir}");

        var findings = new List<object>();
        if (missing > 0)
        {
            findings.Add(new
            {
                severity = "warning",
                category = FindingCategories.Texture,
                code = "texture.unsaved",
                message = $"{missing} variation(s) had no saved texture and were skipped.",
                hint = (string?)"Save them in the Texture tab first.",
                target = (string?)"tab:texture",
            });
        }

        return new
        {
            preset = ExportPresets.TexturePack.Id,
            kind = ExportKind.TexturePack.ToString(),
            experimental = false,
            directory = packDir,
            garments = chosen.Count,
            files,
            status = missing > 0 ? "YELLOW" : "GREEN",
            readBackOk = true,
            warnings = missing,
            errors = 0,
            findings,
            timings = new Dictionary<string, double>(),
        };
    }

    private static object ExportProjectArchive(
        AppSession session, OpenProject project, string outputDir)
    {
        var path = Path.Combine(outputDir,
            project.Document.Name + ProjectService.PackageExtension);

        session.Projects.Save(project);
        session.Projects.SaveAsPackage(project, path);
        session.Log.Export($"Archived project to {path}");

        return new
        {
            preset = ExportPresets.ProjectArchive.Id,
            kind = ExportKind.ProjectArchive.ToString(),
            experimental = false,
            directory = outputDir,
            garments = project.Document.Assets.Count,
            files = new[]
            {
                new { name = Path.GetFileName(path), path, sizeBytes = new FileInfo(path).Length },
            },
            status = "GREEN",
            readBackOk = true,
            warnings = 0,
            errors = 0,
            findings = new List<object>(),
            timings = new Dictionary<string, double>(),
        };
    }

    private static void RecordExport(
        AppSession session, OpenProject project, ExportPreset preset, object result)
    {
        try
        {
            var json = JsonSerializer.SerializeToElement(result, HostBridge.Json);
            var directory = json.TryGetProperty("directory", out var d) ? d.GetString() ?? "" : "";
            var status = json.TryGetProperty("status", out var s) ? s.GetString() ?? "GREEN" : "GREEN";

            var files = json.TryGetProperty("files", out var f) && f.ValueKind == JsonValueKind.Array
                ? f.EnumerateArray().ToList()
                : new List<JsonElement>();

            var record = new ExportRecord
            {
                ResourceName = project.Document.Export.ResourceName,
                Directory = directory,
                Preset = preset.Id,
                FileCount = files.Count,
                TotalBytes = files.Sum(e =>
                    e.TryGetProperty("sizeBytes", out var b) && b.TryGetInt64(out var v) ? v : 0),
                Status = status,
            };

            var history = project.Document.Export.History;
            history.Insert(0, record);
            while (history.Count > 20) history.RemoveAt(history.Count - 1);

            session.Projects.Save(project);
        }
        catch (Exception ex)
        {
            // History is a convenience. Failing to record one must never turn a
            // successful export into an error the user sees.
            session.Log.Warn($"Export history could not be recorded: {ex.Message}");
        }
    }

    private static string BuildValidationReport(ClothingProject document, AddonExportResult result)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"# Validation report - {document.Export.ResourceName}");
        sb.AppendLine();
        sb.AppendLine($"- Generated: {DateTimeOffset.Now:u}");
        sb.AppendLine($"- Status: **{result.Validation.Status}**");
        sb.AppendLine($"- Files written: {result.Files.Count}");
        sb.AppendLine($"- All files re-parsed: {(result.ReadBack.AllOk ? "yes" : "no")}");
        sb.AppendLine();
        sb.AppendLine("> This resource has **not** been loaded by a running FiveM client.");
        sb.AppendLine("> Treat it as experimental until it has been tested in game.");
        sb.AppendLine();

        if (result.Validation.Findings.Count == 0)
        {
            sb.AppendLine("No findings.");
            return sb.ToString();
        }

        sb.AppendLine("| Severity | Category | Code | Message |");
        sb.AppendLine("|---|---|---|---|");
        foreach (var f in result.Validation.Findings.OrderByDescending(f => f.Severity))
            sb.AppendLine($"| {f.Severity} | {f.Category} | `{f.Code}` | {f.Message} |");

        return sb.ToString();
    }

    // ------------------------------------------------------------------
    // shell integration
    // ------------------------------------------------------------------

    private static void RegisterShell(
        HostBridge bridge, AppSession session, Func<System.Windows.Window?> window)
    {
        bridge.On("shell.openFolder", args =>
        {
            var path = HostBridge.RequiredString(args, "path");
            if (!Directory.Exists(path) && !File.Exists(path))
                throw new EditorException("not_found", "That folder no longer exists.");

            var target = Directory.Exists(path) ? path : Path.GetDirectoryName(path)!;
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{target}\"") { UseShellExecute = true });
            return true;
        });

        bridge.On("shell.reveal", args =>
        {
            var path = HostBridge.RequiredString(args, "path");
            if (!File.Exists(path) && !Directory.Exists(path))
                throw new EditorException("not_found", "That item no longer exists.");

            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"")
            { UseShellExecute = true });
            return true;
        });

        bridge.On("shell.copyPath", args =>
        {
            var path = HostBridge.RequiredString(args, "path");
            System.Windows.Clipboard.SetText(path);
            return true;
        });
    }

    /// <summary>
    /// Gate for operations that expose raw bytes.
    /// </summary>
    /// <remarks>
    /// Not a security boundary -- the user owns the machine and the files. It
    /// keeps a debugging surface out of the way of people who did not ask for
    /// it, and makes the hex view's read-only nature deliberate rather than
    /// incidental.
    /// </remarks>
    private static void RequireDeveloperMode(AppSession session)
    {
        if (session.Settings.Current.DeveloperMode) return;
        throw new EditorException("developer_only",
            "Raw binary inspection is a developer-mode tool.",
            "Turn on Settings > Developer > Developer mode to use it.");
    }

    private static string Version() =>
        Assembly.GetExecutingAssembly().GetName().Version is { } v
            ? $"{v.Major}.{v.Minor}.{v.Build}"
            : "0.0.0";
}

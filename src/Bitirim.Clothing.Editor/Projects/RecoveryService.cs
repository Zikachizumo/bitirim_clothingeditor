using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bitirim.Clothing.Editor.Infrastructure;

namespace Bitirim.Clothing.Editor.Projects;

/// <summary>A project that was open when the application stopped without closing it.</summary>
public sealed class RecoveryRecord
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";

    /// <summary>The folder the project lives in.</summary>
    public string Directory { get; set; } = "";

    /// <summary>The .bitirimclothing file, when the project was opened from one.</summary>
    public string? PackagePath { get; set; }

    public DateTimeOffset At { get; set; } = DateTimeOffset.Now;

    /// <summary>The document as it stood at the last snapshot.</summary>
    public string Snapshot { get; set; } = "";
}

/// <summary>
/// Crash recovery for open projects.
/// </summary>
/// <remarks>
/// While a project is open, a snapshot of its document sits under
/// <c>recovery/</c>. Closing the project cleanly removes it. Anything left
/// behind at startup therefore means the application stopped without closing --
/// a crash, a power cut, or the process being killed.
///
/// A leftover record is only <em>offered</em> to the user when it differs from
/// what is on disk. If they match, nothing was lost and there is nothing to
/// ask about, so the record is discarded quietly rather than manufacturing a
/// scary dialog out of a clean shutdown that merely skipped the last step.
/// </remarks>
public sealed class RecoveryService
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly LogService _log;
    private readonly string _root;

    public RecoveryService(LogService log, string? rootOverride = null)
    {
        _log = log;
        _root = rootOverride ?? Paths.Recovery;
        Directory.CreateDirectory(_root);
    }

    private string FileFor(string key) => Path.Combine(_root, key + ".json");

    /// <summary>A stable, filesystem-safe key for a project location.</summary>
    public static string KeyFor(OpenProject project)
    {
        var identity = (project.PackagePath ?? project.Directory).ToLowerInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }

    /// <summary>Writes or refreshes the snapshot for an open project.</summary>
    public void Snapshot(OpenProject project, string documentJson)
    {
        try
        {
            var record = new RecoveryRecord
            {
                Key = KeyFor(project),
                Name = project.Document.Name,
                Directory = project.Directory,
                PackagePath = project.PackagePath,
                At = DateTimeOffset.Now,
                Snapshot = documentJson,
            };

            var path = FileFor(record.Key);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(record, Json));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            // Recovery is a safety net, not a feature the user asked for. If it
            // cannot be written, that must never stop them working.
            _log.Warn($"Recovery snapshot could not be written: {ex.Message}");
        }
    }

    /// <summary>Removes the snapshot after a clean close.</summary>
    public void Clear(OpenProject project) => Clear(KeyFor(project));

    public void Clear(string key)
    {
        try
        {
            var path = FileFor(key);
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            _log.Warn($"Recovery snapshot could not be removed: {ex.Message}");
        }
    }

    /// <summary>
    /// Snapshots left behind by an unclean shutdown that actually differ from
    /// what is on disk.
    /// </summary>
    public IReadOnlyList<RecoveryRecord> Pending()
    {
        var found = new List<RecoveryRecord>();

        foreach (var file in SafeEnumerate())
        {
            RecoveryRecord? record;
            try
            {
                record = JsonSerializer.Deserialize<RecoveryRecord>(File.ReadAllText(file), Json);
            }
            catch (Exception ex)
            {
                _log.Warn($"Unreadable recovery record discarded: {ex.Message}");
                TryDelete(file);
                continue;
            }

            if (record is null || string.IsNullOrEmpty(record.Directory))
            {
                TryDelete(file);
                continue;
            }

            var projectJson = Path.Combine(record.Directory, "project.json");
            if (!File.Exists(projectJson))
            {
                // The project itself is gone; there is nothing to restore into.
                TryDelete(file);
                continue;
            }

            try
            {
                if (string.Equals(File.ReadAllText(projectJson), record.Snapshot, StringComparison.Ordinal))
                {
                    TryDelete(file);
                    continue;
                }
            }
            catch (IOException)
            {
                // Unreadable right now; leave the record for the next start.
                continue;
            }

            found.Add(record);
        }

        if (found.Count > 0)
            _log.Warn($"{found.Count} project(s) have unsaved work from a previous session.");

        return found.OrderByDescending(r => r.At).ToList();
    }

    /// <summary>
    /// Writes a recovered snapshot over the project on disk, after moving the
    /// current file into <c>backups/</c>.
    /// </summary>
    public string Restore(RecoveryRecord record)
    {
        var projectJson = Path.Combine(record.Directory, "project.json");
        if (!File.Exists(projectJson))
            throw new EditorException("not_found", "That project folder no longer exists.");

        var backupDir = Path.Combine(record.Directory, "backups",
            "before-recovery-" + DateTimeOffset.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
        Directory.CreateDirectory(backupDir);
        File.Copy(projectJson, Path.Combine(backupDir, "project.json"), overwrite: true);

        var temp = projectJson + ".tmp";
        File.WriteAllText(temp, record.Snapshot);
        File.Move(temp, projectJson, overwrite: true);

        Clear(record.Key);
        _log.Info($"Recovered '{record.Name}'; the pre-recovery file is under {backupDir}");
        return record.PackagePath ?? record.Directory;
    }

    private IEnumerable<string> SafeEnumerate()
    {
        try { return Directory.EnumerateFiles(_root, "*.json").ToList(); }
        catch { return Array.Empty<string>(); }
    }

    private void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* best effort */ }
    }
}

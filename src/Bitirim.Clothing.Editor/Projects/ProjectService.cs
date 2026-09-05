using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bitirim.Clothing.Editor.Infrastructure;

namespace Bitirim.Clothing.Editor.Projects;

public sealed record OpenProject(ClothingProject Document, string Directory, string? PackagePath)
{
    public string ProjectJsonPath => Path.Combine(Directory, "project.json");
    public bool IsPackage => PackagePath is not null;

    /// <summary>Notes produced by a schema migration when this project was opened.</summary>
    public IReadOnlyList<string> MigrationNotes { get; init; } = Array.Empty<string>();

    /// <summary>Schema version the file on disk was written with.</summary>
    public int OpenedFromSchema { get; init; } = ClothingProject.CurrentSchemaVersion;
}

/// <summary>
/// Creating, opening and saving clothing projects.
/// </summary>
/// <remarks>
/// A project is a <em>folder</em>. The <c>.bitirimclothing</c> file is that
/// folder zipped, which is what makes a project portable between machines
/// without giving up a sane on-disk layout while it is open.
///
/// Project files carry data only. Nothing in them is ever executed, and paths
/// read out of one are validated to stay inside the project root before use.
/// </remarks>
public sealed class ProjectService
{
    public static readonly string PackageExtension = ".bitirimclothing";

    private static readonly JsonSerializerOptions Json = ProjectSerialization.Options;

    private static readonly string[] SubFolders =
        { "assets", "textures", "layers", "previews", "exports", "backups" };

    private readonly LogService _log;

    public ProjectService(LogService log) => _log = log;

    public OpenProject Create(string parentDirectory, ClothingProject document)
    {
        var dir = Path.Combine(parentDirectory, Sanitize(document.Name));
        var unique = dir;
        var n = 2;
        while (Directory.Exists(unique)) unique = $"{dir} ({n++})";

        Directory.CreateDirectory(unique);
        foreach (var sub in SubFolders) Directory.CreateDirectory(Path.Combine(unique, sub));

        document.CreatedAt = document.UpdatedAt = DateTimeOffset.UtcNow;
        var project = new OpenProject(document, unique, null);
        Save(project);
        _log.Info($"Project created: {unique}");
        return project;
    }

    public OpenProject Open(string path)
    {
        if (Directory.Exists(path))
            return OpenFolder(path, null);

        var ext = Path.GetExtension(path);
        if (string.Equals(ext, PackageExtension, StringComparison.OrdinalIgnoreCase))
            return OpenPackage(path);

        if (string.Equals(Path.GetFileName(path), "project.json", StringComparison.OrdinalIgnoreCase))
            return OpenFolder(Path.GetDirectoryName(path)!, null);

        throw new EditorException("unsupported_project",
            $"'{Path.GetFileName(path)}' is not a Bitirim clothing project.");
    }

    private OpenProject OpenFolder(string directory, string? packagePath)
    {
        var jsonPath = Path.Combine(directory, "project.json");
        if (!File.Exists(jsonPath))
            throw new EditorException("project_missing",
                "This folder does not contain a project.json.");

        ProjectMigrator.MigrationResult migration;
        try
        {
            migration = ProjectMigrator.Load(File.ReadAllText(jsonPath), Json);
        }
        catch (JsonException ex)
        {
            _log.Error("Project parse failed", ex);
            throw new EditorException("project_corrupt",
                "The project file could not be read. It may be corrupted or from a newer version.");
        }

        foreach (var sub in SubFolders) Directory.CreateDirectory(Path.Combine(directory, sub));

        var opened = new OpenProject(migration.Document, directory, packagePath)
        {
            MigrationNotes = migration.Notes,
            OpenedFromSchema = migration.FromVersion,
        };

        if (migration.Migrated)
        {
            // Keep the original before anything can overwrite it. If the
            // migration turns out to be wrong the user still has the file that
            // worked, which is the whole point of migrating rather than
            // refusing to open.
            var preserved = PreserveOriginal(opened, migration.FromVersion);
            _log.Info($"Project migrated from schema {migration.FromVersion} to "
                      + $"{migration.ToVersion}; original kept at {preserved}");
            foreach (var note in migration.Notes) _log.Info($"  migration: {note}");
        }

        _log.Info($"Project opened: {directory}");
        return opened;
    }

    /// <summary>Copies project.json aside before a migration is allowed to overwrite it.</summary>
    private string PreserveOriginal(OpenProject project, int fromVersion)
    {
        try
        {
            var dir = Path.Combine(project.Directory, "backups", $"schema{fromVersion}");
            Directory.CreateDirectory(dir);
            var dest = Path.Combine(dir, "project.json");
            if (!File.Exists(dest)) File.Copy(project.ProjectJsonPath, dest);
            return dest;
        }
        catch (Exception ex)
        {
            _log.Warn($"Could not preserve the pre-migration project file: {ex.Message}");
            return "(not written)";
        }
    }

    private OpenProject OpenPackage(string packagePath)
    {
        var workspace = Path.Combine(Paths.Workspaces, Path.GetFileNameWithoutExtension(packagePath)
                                                      + "_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(workspace);

        using (var archive = ZipFile.OpenRead(packagePath))
        {
            foreach (var entry in archive.Entries)
            {
                if (entry.FullName.EndsWith('/')) continue;

                // Reject any entry that would escape the workspace (zip slip).
                var destination = Path.GetFullPath(Path.Combine(workspace, entry.FullName));
                if (!destination.StartsWith(Path.GetFullPath(workspace) + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase))
                {
                    _log.Warn($"Rejected package entry outside project root: {entry.FullName}");
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                entry.ExtractToFile(destination, overwrite: true);
            }
        }

        return OpenFolder(workspace, packagePath);
    }

    /// <summary>
    /// The document exactly as <see cref="Save"/> would write it.
    /// </summary>
    /// <remarks>
    /// Recovery and undo snapshots compare byte-for-byte against the file on
    /// disk, so they must come from the same serialiser with the same options.
    /// </remarks>
    public static string Serialize(ClothingProject document) =>
        ProjectSerialization.Serialize(document);

    public void Save(OpenProject project)
    {
        project.Document.UpdatedAt = DateTimeOffset.UtcNow;

        // Write to a temp file and swap, so an interrupted save never leaves a
        // half-written project.json behind.
        var target = project.ProjectJsonPath;
        var temp = target + ".tmp";
        File.WriteAllText(temp, Serialize(project.Document));
        File.Move(temp, target, overwrite: true);

        if (project.IsPackage) WritePackage(project, project.PackagePath!);
        _log.Info($"Project saved: {project.Document.Name}");
    }

    public OpenProject SaveAsPackage(OpenProject project, string packagePath)
    {
        WritePackage(project, packagePath);
        _log.Info($"Project packaged: {packagePath}");
        return project with { PackagePath = packagePath };
    }

    private void WritePackage(OpenProject project, string packagePath)
    {
        var temp = packagePath + ".tmp";
        if (File.Exists(temp)) File.Delete(temp);

        using (var zip = ZipFile.Open(temp, ZipArchiveMode.Create))
        {
            foreach (var file in Directory.EnumerateFiles(project.Directory, "*", SearchOption.AllDirectories))
            {
                if (file.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) continue;
                var relative = Path.GetRelativePath(project.Directory, file).Replace('\\', '/');
                if (relative.StartsWith("exports/", StringComparison.OrdinalIgnoreCase)) continue;
                zip.CreateEntryFromFile(file, relative, CompressionLevel.Optimal);
            }
        }

        File.Move(temp, packagePath, overwrite: true);
    }

    /// <summary>Timestamped copy of project.json, kept under backups/.</summary>
    public string Backup(OpenProject project)
    {
        var dir = Path.Combine(project.Directory, "backups",
            DateTimeOffset.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
        Directory.CreateDirectory(dir);
        var dest = Path.Combine(dir, "project.json");
        File.Copy(project.ProjectJsonPath, dest, overwrite: true);
        return dest;
    }

    /// <summary>
    /// Resolves a project-relative path, refusing anything that escapes the root.
    /// </summary>
    public static string ResolveInProject(OpenProject project, string relative)
    {
        var root = Path.GetFullPath(project.Directory);
        var full = Path.GetFullPath(Path.Combine(root, relative));
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
        {
            throw new EditorException("path_escape",
                "That file is outside the project folder and cannot be used.");
        }
        return full;
    }

    /// <summary>Copies an external file into the project so it stays portable.</summary>
    public static string ImportInto(OpenProject project, string sourcePath, string subFolder)
    {
        var dir = Path.Combine(project.Directory, subFolder);
        Directory.CreateDirectory(dir);

        var name = Path.GetFileName(sourcePath);
        var destination = Path.Combine(dir, name);
        var n = 2;
        while (File.Exists(destination) && !SameFile(sourcePath, destination))
        {
            destination = Path.Combine(dir,
                $"{Path.GetFileNameWithoutExtension(name)} ({n++}){Path.GetExtension(name)}");
        }

        File.Copy(sourcePath, destination, overwrite: true);
        return Path.GetRelativePath(project.Directory, destination).Replace('\\', '/');
    }

    private static bool SameFile(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    private static string Sanitize(string name)
    {
        var cleaned = new string(name.Select(
            c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "Untitled" : cleaned;
    }
}

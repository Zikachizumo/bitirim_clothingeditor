using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bitirim.Clothing.Editor.Infrastructure;

namespace Bitirim.Clothing.Editor.Settings;

public sealed class AppSettings
{
    // General
    public string? DefaultProjectFolder { get; set; }
    public string? DefaultExportFolder { get; set; }
    public string? AssetLibraryPath { get; set; }
    public string? FiveMServerPath { get; set; }
    public List<string> RecentProjects { get; set; } = new();

    // Appearance
    public string Theme { get; set; } = "dark";
    public double UiScale { get; set; } = 1.0;

    // Editor
    public int AutosaveMinutes { get; set; } = 5;
    public bool AutosaveEnabled { get; set; } = true;
    public int UndoDepth { get; set; } = 100;

    // Viewport
    public bool ShowGrid { get; set; } = true;
    public bool ShowAxis { get; set; } = true;
    public string ShadingMode { get; set; } = "material";

    // Export
    public string DefaultTextureFormat { get; set; } = "BC3";
    public string DefaultManifestMode { get; set; } = "Stream";
    public bool BackupBeforeOverwrite { get; set; } = true;

    // AI

    /// <summary>Provider id. Only "gemini" is implemented.</summary>
    public string AiProvider { get; set; } = "gemini";
    public string? AiModel { get; set; }

    /// <summary>
    /// DPAPI-protected API key, base64. Never the plaintext key.
    /// </summary>
    public string? AiApiKeyProtected { get; set; }

    // Advanced
    public bool DeveloperMode { get; set; }
    public bool ShowMockAssets { get; set; } = true;
}

/// <summary>
/// Loads and saves application settings.
/// </summary>
/// <remarks>
/// The AI API key is encrypted with Windows DPAPI scoped to the current user,
/// so it is unreadable by other accounts and never sits on disk in plaintext.
/// It is also never returned to the UI -- only whether one is set.
/// </remarks>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly LogService _log;
    private readonly string _path;

    public SettingsService(LogService log)
    {
        _log = log;
        _path = Path.Combine(Paths.Settings, "settings.json");
        Current = Load();
    }

    public AppSettings Current { get; private set; }

    private AppSettings Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), Json);
                if (loaded is not null) return Normalise(loaded);
            }
        }
        catch (Exception ex)
        {
            _log.Error("Settings could not be read; defaults will be used.", ex);
        }

        return Normalise(new AppSettings());
    }

    private static AppSettings Normalise(AppSettings s)
    {
        s.DefaultProjectFolder ??= Paths.DefaultProjectsFolder;
        s.AutosaveMinutes = Math.Clamp(s.AutosaveMinutes, 1, 120);
        s.UndoDepth = Math.Clamp(s.UndoDepth, 10, 1000);
        s.UiScale = Math.Clamp(s.UiScale, 0.75, 2.0);

        // Settings written by an earlier build name providers that were never
        // implemented. Carrying those forward would disable a feature that
        // works, so they fall back to the one that exists.
        if (s.AiProvider is not "gemini") s.AiProvider = "gemini";
        return s;
    }

    public void Save()
    {
        try
        {
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(Current, Json));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex)
        {
            _log.Error("Settings could not be saved.", ex);
        }
    }

    public void Update(Action<AppSettings> mutate)
    {
        mutate(Current);
        Normalise(Current);
        Save();
    }

    public void AddRecentProject(string path)
    {
        Current.RecentProjects.RemoveAll(
            p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        Current.RecentProjects.Insert(0, path);
        while (Current.RecentProjects.Count > 12)
            Current.RecentProjects.RemoveAt(Current.RecentProjects.Count - 1);
        Save();
    }

    public bool HasAiApiKey => !string.IsNullOrEmpty(Current.AiApiKeyProtected);

    /// <summary>
    /// Settings as anything outside the host may see them: everything except
    /// the key.
    /// </summary>
    /// <remarks>
    /// The stored value is DPAPI ciphertext rather than the key itself, but it
    /// is still key material and it has no business crossing into a web view,
    /// a devtools inspector or a bug report. Removing it here, rather than
    /// marking the property ignored, keeps it in settings.json where it
    /// belongs -- both paths use the same serialiser.
    ///
    /// Whether a key exists is reported separately, by <see cref="HasAiApiKey"/>.
    /// </remarks>
    public JsonNode PublicView()
    {
        var node = JsonSerializer.SerializeToNode(Current, Json)!;
        node.AsObject().Remove(
            JsonNamingPolicy.CamelCase.ConvertName(nameof(AppSettings.AiApiKeyProtected)));
        return node;
    }

    public void SetAiApiKey(string? plaintext)
    {
        if (string.IsNullOrWhiteSpace(plaintext))
        {
            Current.AiApiKeyProtected = null;
            Save();
            return;
        }

        var protectedBytes = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(plaintext), optionalEntropy: null, DataProtectionScope.CurrentUser);
        Current.AiApiKeyProtected = Convert.ToBase64String(protectedBytes);
        Save();
        _log.Info("AI API key stored (DPAPI, current user).");
    }

    /// <summary>
    /// Decrypts the API key for an outbound request. Called only at the moment
    /// of use; the value is never sent to the UI layer.
    /// </summary>
    public string? RevealAiApiKey()
    {
        if (string.IsNullOrEmpty(Current.AiApiKeyProtected)) return null;
        try
        {
            var bytes = ProtectedData.Unprotect(
                Convert.FromBase64String(Current.AiApiKeyProtected), null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception ex)
        {
            _log.Error("Stored AI key could not be decrypted; it may have been saved by another user account.", ex);
            return null;
        }
    }
}

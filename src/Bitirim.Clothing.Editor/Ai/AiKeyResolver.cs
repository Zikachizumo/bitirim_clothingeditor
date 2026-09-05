using Bitirim.Clothing.Editor.Infrastructure;
using Bitirim.Clothing.Editor.Settings;

namespace Bitirim.Clothing.Editor.Ai;

/// <param name="Source">"environment", ".env" or "settings". Safe to display.</param>
public sealed record ResolvedKey(string Value, string Source);

/// <summary>
/// Finds the Gemini API key, without ever handing it upwards.
/// </summary>
/// <remarks>
/// Three places, in order:
/// <list type="number">
///   <item>the <c>GEMINI_API_KEY</c> environment variable,</item>
///   <item>a <c>.env</c> (or <c>.env.local</c>) file near the application,</item>
///   <item>the DPAPI-protected key entered in Settings &gt; AI.</item>
/// </list>
/// The environment comes first because that is the developer's override and it
/// should win over whatever happens to be saved; the settings entry is what an
/// installed copy actually uses, because there is no shell to export from.
///
/// The key is read at the moment of an outbound request and is never returned
/// to the interface, never written to a log, never put in an error message and
/// never serialised into a project or an export.
/// </remarks>
public static class AiKeyResolver
{
    public const string EnvVariable = "GEMINI_API_KEY";

    /// <summary>Files consulted, in order, within each candidate directory.</summary>
    private static readonly string[] EnvFileNames = { ".env.local", ".env" };

    /// <summary>How far up from a starting directory we look for a .env.</summary>
    private const int MaxWalkUp = 5;

    public static ResolvedKey? Resolve(SettingsService? settings)
    {
        var fromEnvironment = Environment.GetEnvironmentVariable(EnvVariable);
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
            return new ResolvedKey(fromEnvironment.Trim(), "environment");

        var fromFile = FromEnvFile();
        if (fromFile is not null) return fromFile;

        var stored = settings?.RevealAiApiKey();
        return string.IsNullOrWhiteSpace(stored) ? null : new ResolvedKey(stored.Trim(), "settings");
    }

    /// <summary>Whether a key exists, without materialising it in a caller's variable.</summary>
    public static bool IsConfigured(SettingsService? settings) => Resolve(settings) is not null;

    private static ResolvedKey? FromEnvFile()
    {
        foreach (var directory in CandidateDirectories())
        {
            foreach (var name in EnvFileNames)
            {
                var path = Path.Combine(directory, name);
                if (!File.Exists(path)) continue;

                var value = ReadKey(path, EnvVariable);
                if (!string.IsNullOrWhiteSpace(value))
                    return new ResolvedKey(value.Trim(), ".env");
            }
        }

        return null;
    }

    private static IEnumerable<string> CandidateDirectories()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var current = start;
            for (var depth = 0; depth < MaxWalkUp && !string.IsNullOrEmpty(current); depth++)
            {
                if (seen.Add(current)) yield return current;
                current = Path.GetDirectoryName(current.TrimEnd(Path.DirectorySeparatorChar));
            }
        }

        if (seen.Add(Paths.Root)) yield return Paths.Root;
    }

    /// <summary>
    /// Reads one name out of a dotenv file.
    /// </summary>
    /// <remarks>
    /// Deliberately minimal: <c>NAME=value</c>, <c>#</c> comments, an optional
    /// <c>export</c> prefix and optional surrounding quotes. No interpolation,
    /// no multi-line values -- a fuller parser would be more surface for no
    /// gain, since the file holds exactly one secret.
    /// </remarks>
    internal static string? ReadKey(string path, string name)
    {
        string[] lines;
        try
        {
            lines = File.ReadAllLines(path);
        }
        catch
        {
            // An unreadable .env is not an error worth stopping for: the next
            // candidate, or the stored key, may still work.
            return null;
        }

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            if (line.StartsWith("export ", StringComparison.Ordinal)) line = line[7..].TrimStart();

            var equals = line.IndexOf('=');
            if (equals <= 0) continue;
            if (!string.Equals(line[..equals].Trim(), name, StringComparison.Ordinal)) continue;

            var value = line[(equals + 1)..].Trim();
            if (value.Length >= 2
                && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
            {
                value = value[1..^1];
            }

            return value;
        }

        return null;
    }
}

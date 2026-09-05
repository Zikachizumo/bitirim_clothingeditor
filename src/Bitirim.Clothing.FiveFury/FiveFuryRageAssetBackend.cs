using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Bitirim.Clothing.Core.Rage;

namespace Bitirim.Clothing.FiveFury;

/// <summary>
/// <see cref="IRageAssetBackend"/> backed by the Python asset service.
/// </summary>
/// <remarks>
/// Supervises one long-lived child process and speaks newline-delimited
/// JSON-RPC to it over stdio. The service is stateless between calls, so a
/// crashed process can be restarted transparently without losing work.
///
/// Bulk texture data is passed by temp file rather than inlined in JSON: a
/// 2048x2048 BC7 surface is ~5.6 MB, and base64 in a JSON envelope would be
/// both slower and larger than writing it to disk once.
/// </remarks>
public sealed class FiveFuryRageAssetBackend : IRageAssetBackend
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _pythonExe;
    private readonly string _servicePath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<string> _tempFiles = new();

    private Process? _process;
    private StreamWriter? _stdin;
    private StreamReader? _stdout;
    private int _nextId;

    public FiveFuryRageAssetBackend(string pythonExe, string servicePath)
    {
        _pythonExe = pythonExe;
        _servicePath = servicePath;
    }

    /// <summary>Anything written to the service's stderr, for the host's error log.</summary>
    public List<string> Diagnostics { get; } = new();

    private void EnsureStarted()
    {
        if (_process is { HasExited: false }) return;

        var psi = new ProcessStartInfo(_pythonExe)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardInputEncoding = new UTF8Encoding(false),
            WorkingDirectory = Path.GetDirectoryName(_servicePath)!,
        };
        psi.ArgumentList.Add(_servicePath);

        _process = Process.Start(psi)
                   ?? throw new RageAssetException("internal", "Asset service failed to start.");
        _stdin = _process.StandardInput;
        _stdout = _process.StandardOutput;

        _process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
                lock (Diagnostics) Diagnostics.Add(e.Data);
        };
        _process.BeginErrorReadLine();
    }

    private async Task<JsonElement> CallAsync(string op, object args, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            EnsureStarted();
            var id = Interlocked.Increment(ref _nextId);
            var payload = JsonSerializer.Serialize(new { id, op, args }, Json);

            await _stdin!.WriteLineAsync(payload.AsMemory(), ct).ConfigureAwait(false);
            await _stdin.FlushAsync(ct).ConfigureAwait(false);

            var line = await _stdout!.ReadLineAsync(ct).ConfigureAwait(false);
            if (line is null)
                throw new RageAssetException("internal", "Asset service closed unexpectedly.");

            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (!root.GetProperty("ok").GetBoolean())
            {
                var err = root.GetProperty("error");
                throw new RageAssetException(
                    err.GetProperty("code").GetString() ?? "internal",
                    err.GetProperty("message").GetString() ?? "Unknown error.");
            }

            return root.GetProperty("result").Clone();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<BackendCapabilities> GetCapabilitiesAsync(CancellationToken ct = default)
    {
        var r = await CallAsync("capabilities", new { }, ct).ConfigureAwait(false);
        var caps = r.GetProperty("capabilities");
        bool Flag(string n) => caps.TryGetProperty(n, out var v) && v.GetBoolean();

        return new BackendCapabilities(
            r.GetProperty("contractVersion").GetString()!,
            r.GetProperty("backend").GetString()!,
            r.GetProperty("backendVersion").GetString()!,
            r.GetProperty("python").GetString()!,
            r.GetProperty("operations").EnumerateArray().Select(x => x.GetString()!).ToList(),
            Flag("readYdd"), Flag("readYtd"), Flag("readYmt"),
            Flag("writeYtd"), Flag("writeYmt"), Flag("writeYdd"),
            Flag("writeYddGeometry"), Flag("encodeTexture"));
    }

    public async Task<DrawableDictionaryInfo> ReadYddAsync(string path, CancellationToken ct = default)
        => Deserialize<DrawableDictionaryInfo>(
            await CallAsync("ydd_read", new { path }, ct).ConfigureAwait(false));

    public async Task<TextureDictionaryInfo> ReadYtdAsync(string path, CancellationToken ct = default)
        => Deserialize<TextureDictionaryInfo>(
            await CallAsync("ytd_read", new { path }, ct).ConfigureAwait(false));

    public async Task<PedMetadataInfo> ReadYmtAsync(string path, CancellationToken ct = default)
        => Deserialize<PedMetadataInfo>(
            await CallAsync("ymt_read", new { path }, ct).ConfigureAwait(false));

    public async Task<MeshInfo> ExtractMeshAsync(
        string path, string blobPath, string lod = "high", CancellationToken ct = default)
        => Deserialize<MeshInfo>(
            await CallAsync("ydd_mesh", new { path, blob = blobPath, lod }, ct).ConfigureAwait(false));

    public async Task<TextureInfo> DecodeTextureAsync(
        string path, string blobPath, int index = 0, CancellationToken ct = default)
        => Deserialize<TextureInfo>(
            await CallAsync("ytd_decode", new { path, blob = blobPath, index }, ct).ConfigureAwait(false));

    public async Task<WriteResult> ReplaceYtdTextureAsync(
        string sourcePath, string destinationPath, EncodedTexture texture,
        string? textureName = null, CancellationToken ct = default)
    {
        var blob = NewTempFile(".bin");
        await File.WriteAllBytesAsync(blob, texture.Data, ct).ConfigureAwait(false);

        var r = await CallAsync("ytd_replace_texture", new
        {
            source = sourcePath,
            destination = destinationPath,
            blob,
            width = texture.Width,
            height = texture.Height,
            format = texture.Format,
            mipCount = texture.MipCount,
            name = textureName,
        }, ct).ConfigureAwait(false);

        return ToWriteResult(r);
    }

    public async Task<WriteResult> MakeEmissiveAsync(
        string sourcePath, string destinationPath, double multiplier = 1.0,
        CancellationToken ct = default)
    {
        var r = await CallAsync("ydd_make_emissive", new
        {
            source = sourcePath,
            destination = destinationPath,
            multiplier,
        }, ct).ConfigureAwait(false);

        return ToWriteResult(r);
    }

    public async Task<WriteResult> BuildAddonYmtAsync(
        string templatePath, string destinationPath, int component, int textureCount,
        uint dlcNameHash = 0, CancellationToken ct = default)
    {
        var r = await CallAsync("ymt_build_addon", new
        {
            template = templatePath,
            destination = destinationPath,
            component,
            textureCount,
            dlcNameHash,
        }, ct).ConfigureAwait(false);

        return ToWriteResult(r);
    }

    public async Task<PackValidationResult> ValidatePackAsync(
        IReadOnlyList<string> files, CancellationToken ct = default)
    {
        var r = await CallAsync("validate_pack", new { files }, ct).ConfigureAwait(false);
        var list = r.GetProperty("files").EnumerateArray().Select(f => new PackFileResult(
            f.GetProperty("file").GetString()!,
            f.GetProperty("ok").GetBoolean(),
            f.TryGetProperty("kind", out var k) ? k.GetString() : null,
            f.TryGetProperty("sizeBytes", out var s) ? s.GetInt64() : 0,
            f.TryGetProperty("error", out var e) ? e.GetString() : null)).ToList();

        return new PackValidationResult(r.GetProperty("allOk").GetBoolean(), list);
    }

    private static WriteResult ToWriteResult(JsonElement r) => new(
        r.GetProperty("path").GetString()!,
        r.GetProperty("sizeBytes").GetInt64(),
        r.TryGetProperty("issues", out var i)
            ? i.EnumerateArray().Select(x => x.GetString() ?? "").ToList()
            : Array.Empty<string>(),
        r.TryGetProperty("elapsedMs", out var ms) ? ms.GetDouble() : 0);

    private static T Deserialize<T>(JsonElement e)
        => e.Deserialize<T>(Json)
           ?? throw new RageAssetException("internal", "Malformed response from asset service.");

    private string NewTempFile(string extension)
    {
        var path = Path.Combine(Path.GetTempPath(), $"bcc_{Guid.NewGuid():N}{extension}");
        lock (_tempFiles) _tempFiles.Add(path);
        return path;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_process is { HasExited: false })
            {
                _stdin?.Close();
                if (!_process.WaitForExit(2000)) _process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Disposal must not throw; a stuck child is killed above.
        }

        lock (_tempFiles)
        {
            foreach (var f in _tempFiles)
            {
                try { if (File.Exists(f)) File.Delete(f); } catch { /* best effort */ }
            }
            _tempFiles.Clear();
        }

        _process?.Dispose();
        _gate.Dispose();
        await Task.CompletedTask;
    }
}

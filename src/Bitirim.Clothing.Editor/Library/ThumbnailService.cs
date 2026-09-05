using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Bitirim.Clothing.Core.Rage;
using Bitirim.Clothing.Editor.Infrastructure;
using Bitirim.Clothing.Textures;

namespace Bitirim.Clothing.Editor.Library;

/// <summary>What a thumbnail request produced.</summary>
/// <param name="State">
/// <c>ready</c>, <c>pending</c>, or <c>unavailable</c>. Never a silent failure:
/// a card that cannot be rendered says so rather than showing a blank square.
/// </param>
public sealed record ThumbnailResult(string State, string? Path, string? Reason);

/// <summary>
/// Generates and caches drawable thumbnails.
/// </summary>
/// <remarks>
/// Cache keys include the file's size and last-write time, so an asset that is
/// replaced on disk re-renders while an unchanged one is never decoded twice.
/// That was the explicit requirement: opening the library must not re-parse the
/// whole folder every time.
///
/// Rendering runs off the UI thread, one asset at a time per process, and is
/// bounded by <see cref="MaxConcurrent"/> because each render round-trips
/// through the Python asset backend.
/// </remarks>
public sealed class ThumbnailService
{
    private const int MaxConcurrent = 2;
    public const int Size = 256;

    private readonly LogService _log;
    private readonly string _cacheRoot;
    private readonly SemaphoreSlim _gate = new(MaxConcurrent, MaxConcurrent);

    /// <summary>Renders already running, so two cards asking at once share one.</summary>
    private readonly ConcurrentDictionary<string, Task<ThumbnailResult>> _inFlight = new();

    /// <summary>Assets known not to render, so we do not retry them every scan.</summary>
    private readonly ConcurrentDictionary<string, string> _failed = new();

    public ThumbnailService(LogService log, string? cacheRoot = null)
    {
        _log = log;
        _cacheRoot = cacheRoot ?? Paths.Thumbnails;
        Directory.CreateDirectory(_cacheRoot);
    }

    /// <summary>Cache key for a file, invalidated by size or modification time.</summary>
    public static string KeyFor(string path)
    {
        var info = new FileInfo(path);
        var identity = $"{path.ToLowerInvariant()}|{info.Length}|{info.LastWriteTimeUtc.Ticks}|v2|{Size}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..24]
            .ToLowerInvariant();
    }

    private string PathFor(string key) => Path.Combine(_cacheRoot, key + ".png");

    /// <summary>The cached thumbnail, without rendering one.</summary>
    public ThumbnailResult Peek(string assetPath)
    {
        if (!File.Exists(assetPath))
            return new ThumbnailResult("unavailable", null, "The file is no longer there.");

        var key = KeyFor(assetPath);
        var cached = PathFor(key);
        if (File.Exists(cached)) return new ThumbnailResult("ready", cached, null);

        return _failed.TryGetValue(key, out var reason)
            ? new ThumbnailResult("unavailable", null, reason)
            : new ThumbnailResult("pending", null, null);
    }

    /// <summary>
    /// Returns the thumbnail, rendering it if this is the first request.
    /// </summary>
    public Task<ThumbnailResult> GetAsync(
        string assetPath,
        Func<CancellationToken, Task<(MeshInfo Mesh, byte[] Blob)>> loadMesh,
        CancellationToken ct = default)
    {
        var peeked = Peek(assetPath);
        if (peeked.State != "pending") return Task.FromResult(peeked);

        var key = KeyFor(assetPath);
        return _inFlight.GetOrAdd(key, _ => RenderAsync(key, assetPath, loadMesh, ct));
    }

    private async Task<ThumbnailResult> RenderAsync(
        string key,
        string assetPath,
        Func<CancellationToken, Task<(MeshInfo Mesh, byte[] Blob)>> loadMesh,
        CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var destination = PathFor(key);
            if (File.Exists(destination)) return new ThumbnailResult("ready", destination, null);

            var (mesh, blob) = await loadMesh(ct).ConfigureAwait(false);
            var geometry = MeshThumbnailRenderer.ReadBlob(blob, mesh.ByteLayout);

            var rendered = MeshThumbnailRenderer.Render(geometry, destination, Size);
            if (!rendered)
            {
                const string reason = "This drawable has no renderable geometry.";
                _failed[key] = reason;
                return new ThumbnailResult("unavailable", null, reason);
            }

            return new ThumbnailResult("ready", destination, null);
        }
        catch (OperationCanceledException)
        {
            return new ThumbnailResult("pending", null, null);
        }
        catch (Exception ex)
        {
            var reason = ex is EditorException editor
                ? editor.Message
                : "A preview could not be rendered for this drawable.";
            _failed[key] = reason;
            _log.Warn($"Thumbnail failed for {Path.GetFileName(assetPath)}: {ex.Message}");
            return new ThumbnailResult("unavailable", null, reason);
        }
        finally
        {
            _inFlight.TryRemove(key, out _);
            _gate.Release();
        }
    }

    /// <summary>Deletes every cached thumbnail. Offered in Settings &gt; Performance.</summary>
    public (int Files, long Bytes) Clear()
    {
        var files = 0;
        long bytes = 0;

        try
        {
            foreach (var file in Directory.EnumerateFiles(_cacheRoot, "*.png"))
            {
                try
                {
                    bytes += new FileInfo(file).Length;
                    File.Delete(file);
                    files++;
                }
                catch { /* in use */ }
            }
        }
        catch { /* nothing cached */ }

        _failed.Clear();
        _log.Info($"Thumbnail cache cleared: {files} file(s), {bytes / 1024} KB.");
        return (files, bytes);
    }

    /// <summary>Current cache size, for the settings screen.</summary>
    public (int Files, long Bytes) Usage()
    {
        try
        {
            var files = Directory.EnumerateFiles(_cacheRoot, "*.png").ToList();
            return (files.Count, files.Sum(f => new FileInfo(f).Length));
        }
        catch
        {
            return (0, 0);
        }
    }
}

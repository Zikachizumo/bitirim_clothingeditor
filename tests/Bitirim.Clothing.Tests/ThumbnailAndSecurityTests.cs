using System.IO.Compression;
using System.Text;
using Bitirim.Clothing.Core.Rage;
using Bitirim.Clothing.Editor.Infrastructure;
using Bitirim.Clothing.Editor.Library;
using Bitirim.Clothing.Editor.Projects;
using Bitirim.Clothing.Textures;
using Xunit;

namespace Bitirim.Clothing.Tests;

/// <summary>
/// Thumbnail rendering, binary inspection, and the safety rules around
/// untrusted files.
/// </summary>
public sealed class ThumbnailAndSecurityTests : IDisposable
{
    private readonly string _root;
    private readonly LogService _log = new();

    public ThumbnailAndSecurityTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bcc_thumbtest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    // ------------------------------------------------------------------
    // thumbnail rendering
    // ------------------------------------------------------------------

    /// <summary>A unit cube, laid out exactly as the backend's mesh blob is.</summary>
    private static (byte[] Blob, Dictionary<string, int[]> Layout) CubeBlob()
    {
        float[] positions =
        {
            -0.5f, -0.5f, -0.5f,  0.5f, -0.5f, -0.5f,  0.5f, 0.5f, -0.5f,  -0.5f, 0.5f, -0.5f,
            -0.5f, -0.5f,  0.5f,  0.5f, -0.5f,  0.5f,  0.5f, 0.5f,  0.5f,  -0.5f, 0.5f,  0.5f,
        };

        uint[] indices =
        {
            0, 1, 2, 0, 2, 3,   4, 6, 5, 4, 7, 6,
            0, 4, 5, 0, 5, 1,   3, 2, 6, 3, 6, 7,
            0, 3, 7, 0, 7, 4,   1, 5, 6, 1, 6, 2,
        };

        var positionBytes = new byte[positions.Length * sizeof(float)];
        Buffer.BlockCopy(positions, 0, positionBytes, 0, positionBytes.Length);

        var indexBytes = new byte[indices.Length * sizeof(uint)];
        Buffer.BlockCopy(indices, 0, indexBytes, 0, indexBytes.Length);

        var blob = new byte[positionBytes.Length + indexBytes.Length];
        positionBytes.CopyTo(blob, 0);
        indexBytes.CopyTo(blob, positionBytes.Length);

        var layout = new Dictionary<string, int[]>
        {
            ["positions"] = new[] { 0, positionBytes.Length },
            ["normals"] = new[] { 0, 0 },
            ["uvs"] = new[] { 0, 0 },
            ["indices"] = new[] { positionBytes.Length, indexBytes.Length },
        };

        return (blob, layout);
    }

    [Fact]
    public void THUMBNAIL_TEST_reads_the_backend_blob_layout()
    {
        var (blob, layout) = CubeBlob();

        var geometry = MeshThumbnailRenderer.ReadBlob(blob, layout);

        Assert.Equal(24, geometry.Positions.Length);   // 8 vertices x 3
        Assert.Equal(36, geometry.Indices.Length);     // 12 triangles x 3
        Assert.Null(geometry.Normals);                 // zero-length stream stays null
    }

    [Fact]
    public void THUMBNAIL_TEST_renders_real_geometry_to_a_png()
    {
        var (blob, layout) = CubeBlob();
        var geometry = MeshThumbnailRenderer.ReadBlob(blob, layout);
        var destination = Path.Combine(_root, "cube.png");

        Assert.True(MeshThumbnailRenderer.Render(geometry, destination, 64));
        Assert.True(File.Exists(destination));

        // A PNG, not an empty file dressed up as one.
        var header = File.ReadAllBytes(destination).Take(8).ToArray();
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, header);
    }

    [Fact]
    public void THUMBNAIL_TEST_refuses_rather_than_writing_an_empty_square()
    {
        var empty = new MeshThumbnailRenderer.Geometry(
            Array.Empty<float>(), null, Array.Empty<uint>());
        var destination = Path.Combine(_root, "nothing.png");

        Assert.False(MeshThumbnailRenderer.Render(empty, destination, 64));
        Assert.False(File.Exists(destination));
    }

    [Fact]
    public void THUMBNAIL_TEST_survives_indices_that_point_past_the_vertex_list()
    {
        // A malformed asset must not take the renderer down with it.
        var geometry = new MeshThumbnailRenderer.Geometry(
            new[] { 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f, 0f },
            null,
            new uint[] { 0, 1, 999 });

        var destination = Path.Combine(_root, "bad-indices.png");
        Assert.False(MeshThumbnailRenderer.Render(geometry, destination, 64));
    }

    [Fact]
    public void THUMBNAIL_TEST_survives_a_layout_that_runs_off_the_end_of_the_blob()
    {
        var layout = new Dictionary<string, int[]>
        {
            ["positions"] = new[] { 0, 4096 },
            ["indices"] = new[] { 4096, 4096 },
        };

        var geometry = MeshThumbnailRenderer.ReadBlob(new byte[32], layout);

        Assert.Empty(geometry.Positions);
        Assert.Empty(geometry.Indices);
    }

    [Fact]
    public void THUMBNAIL_CACHE_TEST_keys_change_when_the_file_changes()
    {
        var path = Path.Combine(_root, "asset.ydd");
        File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
        var first = ThumbnailService.KeyFor(path);

        File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4, 5 });
        var second = ThumbnailService.KeyFor(path);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void THUMBNAIL_CACHE_TEST_an_unchanged_file_keeps_its_key()
    {
        var path = Path.Combine(_root, "stable.ydd");
        File.WriteAllBytes(path, new byte[] { 9, 9, 9 });

        Assert.Equal(ThumbnailService.KeyFor(path), ThumbnailService.KeyFor(path));
    }

    [Fact]
    public void THUMBNAIL_CACHE_TEST_a_missing_file_is_unavailable_not_pending()
    {
        var service = new ThumbnailService(_log, Path.Combine(_root, "cache"));

        var result = service.Peek(Path.Combine(_root, "gone.ydd"));

        Assert.Equal("unavailable", result.State);
        Assert.NotNull(result.Reason);
    }

    [Fact]
    public async Task THUMBNAIL_CACHE_TEST_a_backend_failure_is_reported_once_and_remembered()
    {
        var service = new ThumbnailService(_log, Path.Combine(_root, "cache2"));
        var path = Path.Combine(_root, "unreadable.ydd");
        File.WriteAllBytes(path, new byte[] { 0 });

        var attempts = 0;
        Task<(MeshInfo, byte[])> Fail(CancellationToken _)
        {
            attempts++;
            throw new EditorException("parse_failed", "not a drawable");
        }

        var first = await service.GetAsync(path, Fail);
        var second = await service.GetAsync(path, Fail);

        Assert.Equal("unavailable", first.State);
        Assert.Equal("unavailable", second.State);
        Assert.Equal(1, attempts);   // the failure is remembered, not retried per card
    }

    [Fact]
    public void THUMBNAIL_CACHE_TEST_clearing_reports_what_it_removed()
    {
        var cache = Path.Combine(_root, "cache3");
        Directory.CreateDirectory(cache);
        File.WriteAllBytes(Path.Combine(cache, "a.png"), new byte[128]);
        File.WriteAllBytes(Path.Combine(cache, "b.png"), new byte[256]);

        var service = new ThumbnailService(_log, cache);
        var usage = service.Usage();
        Assert.Equal(2, usage.Files);
        Assert.Equal(384, usage.Bytes);

        var cleared = service.Clear();
        Assert.Equal(2, cleared.Files);
        Assert.Equal(0, service.Usage().Files);
    }

    // ------------------------------------------------------------------
    // binary inspection
    // ------------------------------------------------------------------

    [Fact]
    public void HEX_TEST_reads_a_window_and_reports_the_container_magic()
    {
        var path = Path.Combine(_root, "fake.ydd");
        File.WriteAllBytes(path, Encoding.ASCII.GetBytes("RSC7").Concat(new byte[512]).ToArray());

        var window = BinaryInspector.Read(path, 0, 16);

        Assert.Equal("RSC7", window.Magic);
        Assert.Equal(16, window.Length);
        Assert.Equal(516, window.FileSize);
    }

    [Fact]
    public void HEX_TEST_a_window_past_the_end_of_the_file_is_clamped_not_an_error()
    {
        var path = Path.Combine(_root, "small.bin");
        File.WriteAllBytes(path, new byte[10]);

        var window = BinaryInspector.Read(path, 8, 1024);

        Assert.Equal(8, window.Offset);
        Assert.Equal(2, window.Length);
    }

    [Fact]
    public void HEX_TEST_a_request_larger_than_the_cap_is_trimmed()
    {
        var path = Path.Combine(_root, "big.bin");
        File.WriteAllBytes(path, new byte[BinaryInspector.MaxWindow * 2]);

        var window = BinaryInspector.Read(path, 0, BinaryInspector.MaxWindow * 2);

        Assert.Equal(BinaryInspector.MaxWindow, window.Length);
    }

    [Fact]
    public void HEX_TEST_a_missing_file_reports_a_readable_message()
    {
        var ex = Assert.Throws<EditorException>(
            () => BinaryInspector.Read(Path.Combine(_root, "nope.bin"), 0, 16));

        Assert.Equal("not_found", ex.Code);
    }

    [Fact]
    public void HEX_TEST_finds_a_byte_pattern()
    {
        var path = Path.Combine(_root, "haystack.bin");
        var bytes = new byte[4096];
        Encoding.ASCII.GetBytes("jbib_diff_000_a_uni").CopyTo(bytes, 3000);
        File.WriteAllBytes(path, bytes);

        var offset = BinaryInspector.Find(path, Encoding.ASCII.GetBytes("jbib_diff"), 0);

        Assert.Equal(3000, offset);
    }

    [Fact]
    public void HEX_TEST_finds_a_pattern_that_straddles_a_read_chunk()
    {
        // The scanner reads in 1 MB chunks; a match on the boundary is exactly
        // the case a naive implementation misses.
        var path = Path.Combine(_root, "boundary.bin");
        var bytes = new byte[(1 << 20) + 64];
        Encoding.ASCII.GetBytes("BOUNDARY").CopyTo(bytes, (1 << 20) - 4);
        File.WriteAllBytes(path, bytes);

        var offset = BinaryInspector.Find(path, Encoding.ASCII.GetBytes("BOUNDARY"), 0);

        Assert.Equal((1 << 20) - 4, offset);
    }

    [Fact]
    public void HEX_TEST_reports_minus_one_when_a_pattern_is_absent()
    {
        var path = Path.Combine(_root, "plain.bin");
        File.WriteAllBytes(path, new byte[1024]);

        Assert.Equal(-1, BinaryInspector.Find(path, Encoding.ASCII.GetBytes("nothing"), 0));
    }

    [Fact]
    public void HASH_TEST_a_small_file_is_hashed_whole()
    {
        var path = Path.Combine(_root, "hashme.bin");
        File.WriteAllBytes(path, Encoding.ASCII.GetBytes("bitirim"));

        var (hash, partial) = BinaryInspector.Hash(path);

        Assert.False(partial);
        Assert.Equal(64, hash.Length);
    }

    // ------------------------------------------------------------------
    // safety
    // ------------------------------------------------------------------

    [Fact]
    public void SECURITY_TEST_a_package_entry_that_escapes_the_workspace_is_rejected()
    {
        var packagePath = Path.Combine(_root, "evil" + ProjectService.PackageExtension);

        using (var zip = ZipFile.Open(packagePath, ZipArchiveMode.Create))
        {
            var manifest = zip.CreateEntry("project.json");
            using (var writer = new StreamWriter(manifest.Open()))
                writer.Write("""{ "schemaVersion": 2, "name": "Evil", "assets": [] }""");

            // A classic zip-slip payload.
            var escape = zip.CreateEntry("../../pwned.txt");
            using (var writer = new StreamWriter(escape.Open()))
                writer.Write("should never be written");
        }

        var service = new ProjectService(_log);
        var opened = service.Open(packagePath);

        Assert.Equal("Evil", opened.Document.Name);

        // The workspace is two levels below Paths.Workspaces; check both.
        var parent = Directory.GetParent(opened.Directory)!;
        Assert.False(File.Exists(Path.Combine(parent.FullName, "pwned.txt")));
        Assert.False(File.Exists(Path.Combine(parent.Parent!.FullName, "pwned.txt")));
    }

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("..\\..\\windows\\system32\\config\\sam")]
    [InlineData("assets/../../outside.png")]
    [InlineData("C:\\Windows\\System32\\drivers\\etc\\hosts")]
    public void SECURITY_TEST_a_path_that_leaves_the_project_is_refused(string relative)
    {
        var service = new ProjectService(_log);
        var project = service.Create(_root, new ClothingProject { Name = "Guarded" });

        var ex = Assert.Throws<EditorException>(
            () => ProjectService.ResolveInProject(project, relative));
        Assert.Equal("path_escape", ex.Code);
    }

    [Fact]
    public void SECURITY_TEST_a_project_file_is_data_and_never_executed()
    {
        // The document has no field that names a command, and unknown fields are
        // dropped. This asserts the shape rather than the absence of a feature,
        // so it fails loudly if one is ever added.
        var properties = typeof(ClothingProject).GetProperties()
            .Select(p => p.Name.ToLowerInvariant())
            .ToList();

        Assert.DoesNotContain("command", properties);
        Assert.DoesNotContain("script", properties);
        Assert.DoesNotContain("executable", properties);
        Assert.DoesNotContain("onopen", properties);
    }

    [Fact]
    public void SECURITY_TEST_a_corrupt_project_produces_a_message_not_a_parser_dump()
    {
        var directory = Path.Combine(_root, "Corrupt");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "project.json"), "{ \"schemaVersion\": ");

        var ex = Assert.Throws<EditorException>(() => new ProjectService(_log).Open(directory));

        Assert.Equal("project_corrupt", ex.Code);
        Assert.DoesNotContain("JsonException", ex.Message);
        Assert.DoesNotContain("at System.", ex.Message);
    }

    [Fact]
    public void SECURITY_TEST_an_oversized_hex_request_cannot_exhaust_memory()
    {
        var path = Path.Combine(_root, "large.bin");
        File.WriteAllBytes(path, new byte[1 << 20]);

        var window = BinaryInspector.Read(path, 0, int.MaxValue);

        Assert.True(window.Length <= BinaryInspector.MaxWindow);
    }
}

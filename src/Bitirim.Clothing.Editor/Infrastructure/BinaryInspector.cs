using System.Security.Cryptography;

namespace Bitirim.Clothing.Editor.Infrastructure;

/// <summary>A window of bytes plus what could be read about the file around it.</summary>
public sealed record HexWindow(
    string Path,
    long FileSize,
    long Offset,
    int Length,
    string Base64,
    string? Magic);

/// <summary>
/// Read-only binary inspection, for developer mode.
/// </summary>
/// <remarks>
/// Strictly read-only, and strictly bounded: a hex view exists to answer
/// "what is actually in this file" when an export misbehaves, which is the one
/// question a parser cannot answer once it has already failed. There is no
/// write path here at all -- editing a RAGE container by hand would produce a
/// file the game silently rejects, and offering it would be worse than useless.
/// </remarks>
public static class BinaryInspector
{
    /// <summary>Largest window that may be requested at once (64 KB).</summary>
    public const int MaxWindow = 64 * 1024;

    /// <summary>Files above this are hashed by their first and last megabyte only.</summary>
    private const long FullHashLimit = 64L * 1024 * 1024;

    public static HexWindow Read(string path, long offset, int length)
    {
        if (!File.Exists(path))
            throw new EditorException("not_found", $"File not found: {Path.GetFileName(path)}");

        var info = new FileInfo(path);
        if (offset < 0) offset = 0;
        if (offset > info.Length) offset = info.Length;

        length = Math.Clamp(length, 0, MaxWindow);
        if (offset + length > info.Length) length = (int)(info.Length - offset);

        var buffer = new byte[length];
        using (var stream = File.OpenRead(path))
        {
            stream.Seek(offset, SeekOrigin.Begin);
            var read = 0;
            while (read < length)
            {
                var got = stream.Read(buffer, read, length - read);
                if (got <= 0) break;
                read += got;
            }
            if (read != length) Array.Resize(ref buffer, read);
        }

        return new HexWindow(
            path, info.Length, offset, buffer.Length,
            Convert.ToBase64String(buffer),
            ReadMagic(path));
    }

    /// <summary>
    /// Finds the next occurrence of a byte pattern, scanning forward from an offset.
    /// </summary>
    /// <returns>The offset, or -1 when the pattern is not in the rest of the file.</returns>
    public static long Find(string path, byte[] pattern, long from)
    {
        if (pattern.Length == 0) return -1;
        if (!File.Exists(path))
            throw new EditorException("not_found", $"File not found: {Path.GetFileName(path)}");

        const int chunk = 1 << 20;
        var overlap = pattern.Length - 1;
        var buffer = new byte[chunk + overlap];

        using var stream = File.OpenRead(path);
        stream.Seek(Math.Max(0, from), SeekOrigin.Begin);
        var basePosition = stream.Position;
        var carried = 0;

        while (true)
        {
            var read = stream.Read(buffer, carried, chunk);
            if (read <= 0) return -1;

            var available = carried + read;
            var limit = available - pattern.Length;

            for (var i = 0; i <= limit; i++)
            {
                var match = true;
                for (var j = 0; j < pattern.Length; j++)
                {
                    if (buffer[i + j] != pattern[j]) { match = false; break; }
                }
                if (match) return basePosition - carried + i;
            }

            // Carry the tail so a match spanning the chunk boundary is not missed.
            carried = Math.Min(overlap, available);
            Array.Copy(buffer, available - carried, buffer, 0, carried);
            basePosition = stream.Position;
        }
    }

    /// <summary>The container magic, e.g. <c>RSC7</c>, or null when unreadable.</summary>
    public static string? ReadMagic(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var head = new byte[4];
            if (stream.Read(head, 0, 4) != 4) return null;
            return head.All(b => b is >= 32 and < 127)
                ? System.Text.Encoding.ASCII.GetString(head)
                : "0x" + Convert.ToHexString(head);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// SHA-256 of the file, or of its first and last megabyte when it is very
    /// large. The result says which was done so it is never mistaken for the
    /// full hash.
    /// </summary>
    public static (string Hash, bool Partial) Hash(string path)
    {
        var info = new FileInfo(path);
        using var stream = File.OpenRead(path);

        if (info.Length <= FullHashLimit)
            return (Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(), false);

        using var sha = SHA256.Create();
        var buffer = new byte[1 << 20];

        var head = stream.Read(buffer, 0, buffer.Length);
        sha.TransformBlock(buffer, 0, head, null, 0);

        stream.Seek(-buffer.Length, SeekOrigin.End);
        var tail = stream.Read(buffer, 0, buffer.Length);
        sha.TransformFinalBlock(buffer, 0, tail);

        return (Convert.ToHexString(sha.Hash!).ToLowerInvariant(), true);
    }
}

using System.Collections.Concurrent;

namespace Bitirim.Clothing.Editor.Infrastructure;

public enum LogLevel { Debug, Info, Warn, Error }

public sealed record LogEntry(DateTimeOffset At, LogLevel Level, string Channel, string Message)
{
    public override string ToString() =>
        $"{At:yyyy-MM-dd HH:mm:ss.fff} [{Level.ToString().ToUpperInvariant(),-5}] {Message}";
}

/// <summary>
/// File and in-memory logging.
/// </summary>
/// <remarks>
/// Users never see a stack trace. Exceptions go to errors.log with a short
/// reference the user can quote; the UI shows a plain sentence and, in
/// developer mode, the detail.
/// </remarks>
public sealed class LogService
{
    private const int MemoryBufferSize = 2000;

    private readonly ConcurrentQueue<LogEntry> _buffer = new();
    private readonly object _fileLock = new();

    public event Action<LogEntry>? Logged;

    public IReadOnlyList<LogEntry> Recent(int max = 500) =>
        _buffer.Reverse().Take(max).Reverse().ToList();

    public void Debug(string message) => Write(LogLevel.Debug, "application", message);
    public void Info(string message) => Write(LogLevel.Info, "application", message);
    public void Warn(string message) => Write(LogLevel.Warn, "application", message);
    public void Export(string message) => Write(LogLevel.Info, "export", message);

    /// <summary>Logs an error and returns a short reference for the user.</summary>
    public string Error(string message, Exception? exception = null)
    {
        var reference = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        Write(LogLevel.Error, "application", $"{message} [ref {reference}]");

        var detail = exception is null
            ? message
            : $"{message}{Environment.NewLine}{exception}";
        Append("errors.log", $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} [ref {reference}] {detail}");

        return reference;
    }

    private void Write(LogLevel level, string channel, string message)
    {
        var entry = new LogEntry(DateTimeOffset.Now, level, channel, message);

        _buffer.Enqueue(entry);
        while (_buffer.Count > MemoryBufferSize) _buffer.TryDequeue(out _);

        Append(channel == "export" ? "export.log" : "application.log", entry.ToString());

        try { Logged?.Invoke(entry); }
        catch { /* a subscriber must never break logging */ }
    }

    private void Append(string fileName, string line)
    {
        try
        {
            lock (_fileLock)
            {
                File.AppendAllText(Path.Combine(Paths.Logs, fileName), line + Environment.NewLine);
            }
        }
        catch
        {
            // Disk full or locked. Losing a log line must not take down the app.
        }
    }
}

/// <summary>
/// An error that already carries a message fit to show a user.
/// </summary>
public sealed class EditorException : Exception
{
    public EditorException(string code, string message, string? hint = null) : base(message)
    {
        Code = code;
        Hint = hint;
    }

    public string Code { get; }
    public string? Hint { get; }
}

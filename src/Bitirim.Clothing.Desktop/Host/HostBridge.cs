using System.Text.Json;
using System.Text.Json.Serialization;
using Bitirim.Clothing.Editor.Infrastructure;

namespace Bitirim.Clothing.Desktop.Host;

public sealed record HostRequest(int Id, string Op, JsonElement Args);

/// <summary>
/// Routes JSON-RPC calls from the WebView to the host.
/// </summary>
/// <remarks>
/// Protocol <c>bitirim-host-v1</c>, newline-free JSON over the WebView message
/// channel:
/// <code>
///   UI  -> host   {"id":1,"op":"project.save","args":{}}
///   host -> UI    {"id":1,"ok":true,"result":{...}}
///   host -> UI    {"id":1,"ok":false,"error":{"code":"...","message":"..."}}
///   host -> UI    {"event":"project.changed","data":{...}}   (unsolicited)
/// </code>
/// Handlers never let a raw exception escape: everything becomes a coded error
/// with a sentence a user can read, and the detail goes to errors.log with a
/// reference the user can quote.
/// </remarks>
public sealed class HostBridge
{
    public const string ProtocolVersion = "bitirim-host-v1";

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly Dictionary<string, Func<JsonElement, Task<object?>>> _handlers = new(StringComparer.Ordinal);
    private readonly LogService _log;

    public HostBridge(LogService log) => _log = log;

    /// <summary>Raised when the host wants to push something to the UI.</summary>
    public event Func<string, Task>? Send;

    public void On(string op, Func<JsonElement, Task<object?>> handler) => _handlers[op] = handler;

    public void On(string op, Func<JsonElement, object?> handler) =>
        _handlers[op] = args => Task.FromResult(handler(args));

    public IReadOnlyCollection<string> Operations => _handlers.Keys;

    public async Task HandleAsync(string raw)
    {
        int id = 0;
        string op = "?";
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            id = root.TryGetProperty("id", out var idEl) ? idEl.GetInt32() : 0;
            op = root.GetProperty("op").GetString() ?? "?";
            var args = root.TryGetProperty("args", out var a) ? a.Clone() : default;

            if (!_handlers.TryGetValue(op, out var handler))
            {
                await Reply(id, false, null, "unknown_op", $"Unknown operation: {op}");
                return;
            }

            var result = await handler(args).ConfigureAwait(false);
            await Reply(id, true, result, null, null);
        }
        catch (EditorException ex)
        {
            await Reply(id, false, null, ex.Code, ex.Message, ex.Hint);
        }
        catch (Core.Rage.RageAssetException ex)
        {
            await Reply(id, false, null, ex.Code, ex.Message);
        }
        catch (OperationCanceledException)
        {
            await Reply(id, false, null, "cancelled", "The operation was cancelled.");
        }
        catch (Exception ex)
        {
            var reference = _log.Error($"Host operation '{op}' failed", ex);
            await Reply(id, false, null, "internal",
                "Something went wrong while handling that request.",
                $"Reference {reference} - see logs/errors.log for detail.");
        }
    }

    private async Task Reply(
        int id, bool ok, object? result, string? code, string? message, string? hint = null)
    {
        var payload = ok
            ? JsonSerializer.Serialize(new { id, ok = true, result }, Json)
            : JsonSerializer.Serialize(
                new { id, ok = false, error = new { code, message, hint } }, Json);

        if (Send is not null) await Send(payload).ConfigureAwait(false);
    }

    /// <summary>Pushes an unsolicited event to the UI.</summary>
    public async Task EmitAsync(string name, object? data = null)
    {
        if (Send is null) return;
        await Send(JsonSerializer.Serialize(new { @event = name, data }, Json)).ConfigureAwait(false);
    }

    public static string? OptionalString(JsonElement args, string name) =>
        args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var v)
        && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    public static string RequiredString(JsonElement args, string name) =>
        OptionalString(args, name)
        ?? throw new EditorException("bad_args", $"Missing required value: {name}");

    public static int OptionalInt(JsonElement args, string name, int fallback) =>
        args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var v)
        && v.TryGetInt32(out var i)
            ? i
            : fallback;

    public static bool OptionalBool(JsonElement args, string name, bool fallback) =>
        args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var v)
        && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False)
            ? v.GetBoolean()
            : fallback;

    public static T? Deserialize<T>(JsonElement args, string name)
    {
        if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty(name, out var v))
            return default;
        return v.Deserialize<T>(Json);
    }
}

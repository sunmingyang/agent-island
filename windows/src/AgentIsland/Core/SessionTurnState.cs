using System.Text;
using System.Text.Json;

namespace AgentIsland.Core;

public readonly record struct SessionTurnStatus(bool IsDone, string? Key, DateTimeOffset? ActivityDate);

/// Classifies the tail of a transcript: is the latest turn finished (the user
/// is "up"), and which event identifies that turn. Direct port of the macOS
/// SessionTurnState — the turn key + activity date drive alarm dedup and the
/// bookkeeping-grace logic in SessionScanner.
public static class SessionTurnState
{
    public static SessionTurnStatus Claude(IReadOnlyList<string> lines)
    {
        for (var i = lines.Count - 1; i >= 0; i--)
        {
            using var doc = Jsonl.TryParseLine(lines[i]);
            if (doc is null) continue;
            var root = doc.RootElement;
            var type = Jsonl.GetString(root, "type");
            switch (type)
            {
                case "assistant":
                {
                    string? stop = null;
                    if (Jsonl.GetObject(root, "message") is { } message)
                        stop = Jsonl.GetString(message, "stop_reason");
                    var isDone = stop is "end_turn" or "stop_sequence" or "stop";
                    return new SessionTurnStatus(isDone, Key(root, lines[i]), Date(root));
                }
                case "user":
                    return new SessionTurnStatus(false, Key(root, lines[i]), Date(root));
                default:
                    continue;
            }
        }
        return new SessionTurnStatus(false, null, null);
    }

    public static SessionTurnStatus Codex(IReadOnlyList<string> lines)
    {
        for (var i = lines.Count - 1; i >= 0; i--)
        {
            using var doc = Jsonl.TryParseLine(lines[i]);
            if (doc is null) continue;
            var root = doc.RootElement;
            var type = Jsonl.GetString(root, "type");
            var payload = Jsonl.GetObject(root, "payload");
            var payloadType = payload is { } p ? Jsonl.GetString(p, "type") : null;
            if (type == "event_msg")
            {
                if (IsCodexUserOrStart(payloadType))
                    return new SessionTurnStatus(false, Key(root, lines[i]), Date(root));
                if (payloadType is "task_complete" or "turn/completed")
                    return new SessionTurnStatus(true, Key(root, lines[i]), Date(root));
            }
            if (type == "response_item"
                && payloadType == "message"
                && payload is { } pm
                && Jsonl.GetString(pm, "role") == "user")
            {
                return new SessionTurnStatus(false, Key(root, lines[i]), Date(root));
            }
        }
        return new SessionTurnStatus(false, null, null);
    }

    private static bool IsCodexUserOrStart(string? type)
    {
        if (type is null) return false;
        return type == "task_started"
            || type == "turn/started"
            || type == "user_message"
            || type.EndsWith("/task_started", StringComparison.Ordinal)
            || type.EndsWith("/user_message", StringComparison.Ordinal);
    }

    private static string Key(JsonElement root, string line)
    {
        if (Jsonl.GetString(root, "uuid") is { Length: > 0 } uuid) return uuid;
        if (Jsonl.GetString(root, "id") is { Length: > 0 } id) return id;
        if (Jsonl.GetObject(root, "payload") is { } payload)
        {
            foreach (var field in new[] { "turn_id", "id", "item_id", "call_id" })
            {
                if (Jsonl.GetString(payload, field) is { Length: > 0 } value) return value;
            }
        }
        var bytes = Encoding.UTF8.GetBytes(line);
        var encoded = Convert.ToBase64String(bytes);
        return encoded.Length <= 160 ? encoded : encoded[..160];
    }

    private static DateTimeOffset? Date(JsonElement root)
    {
        if (Jsonl.GetString(root, "timestamp") is { } timestamp
            && Jsonl.ParseIso8601(timestamp) is { } parsed)
        {
            return parsed;
        }
        if (Jsonl.GetObject(root, "payload") is { } payload)
        {
            foreach (var field in new[] { "completed_at", "started_at" })
            {
                if (Jsonl.GetDouble(payload, field) is { } seconds)
                    return DateTimeOffset.FromUnixTimeMilliseconds((long)(seconds * 1000));
            }
        }
        return null;
    }
}

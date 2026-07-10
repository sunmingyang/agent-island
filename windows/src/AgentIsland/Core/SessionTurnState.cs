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
    public static SessionTurnStatus Claude(IReadOnlyList<string> lines) =>
        ClaudeCore(lines, sidechainIsTheConversation: false);

    /// For agent transcripts, where every line is a sidechain by definition.
    public static SessionTurnStatus ClaudeAgent(IReadOnlyList<string> lines) =>
        ClaudeCore(lines, sidechainIsTheConversation: true);

    private static SessionTurnStatus ClaudeCore(IReadOnlyList<string> lines, bool sidechainIsTheConversation)
    {
        for (var i = lines.Count - 1; i >= 0; i--)
        {
            using var doc = Jsonl.TryParseLine(lines[i]);
            if (doc is null) continue;
            var root = doc.RootElement;
            // Older Claude Code interleaves subagent traffic into the main
            // transcript marked isSidechain — a subagent's end_turn there is
            // not the user's turn and must not classify the main session.
            if (!sidechainIsTheConversation && Jsonl.GetBool(root, "isSidechain") == true) continue;
            var type = Jsonl.GetString(root, "type");
            switch (type)
            {
                case "assistant":
                {
                    string? stop = null;
                    if (Jsonl.GetObject(root, "message") is { } message)
                        stop = Jsonl.GetString(message, "stop_reason");
                    // Claude Code writes rate-limit / API-error lines with the
                    // SAME envelope as a finished turn (type:assistant,
                    // stop_reason:"stop_sequence") but flags them
                    // isApiErrorMessage:true (e.g. "You've hit your session
                    // limit · resets 2:20am"). Treating those as a completed
                    // turn fired a false "it's your turn" alarm on every
                    // rate-limit — mirror the macOS fix and never mark them done.
                    var isApiError = Jsonl.GetBool(root, "isApiErrorMessage") == true;
                    var isDone = !isApiError && stop is "end_turn" or "stop_sequence" or "stop";
                    return new SessionTurnStatus(isDone, Key(root, lines[i]), Date(root));
                }
                case "user":
                    // Agent runs usually end on the final tool result (marked
                    // toolEndsTurn) rather than an assistant stop; without
                    // this a finished agent never reads as done.
                    if (sidechainIsTheConversation && Jsonl.GetBool(root, "toolEndsTurn") == true)
                        return new SessionTurnStatus(true, Key(root, lines[i]), Date(root));
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
                // FromUnixTimeMilliseconds throws on out-of-range input; a
                // corrupt or foreign-unit timestamp must not fault the scan.
                if (Jsonl.GetDouble(payload, field) is { } seconds)
                {
                    var ms = seconds * 1000;
                    if (ms is >= -62_135_596_800_000 and <= 253_402_300_799_999)
                    {
                        return DateTimeOffset.FromUnixTimeMilliseconds((long)ms);
                    }
                }
            }
        }
        return null;
    }
}

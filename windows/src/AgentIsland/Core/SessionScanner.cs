using System.IO;
using System.Text;

namespace AgentIsland.Core;

/// Discovers Claude Code / Claude Desktop / Codex sessions from the local
/// artifacts those tools already write, and classifies each one through a
/// conservative state machine. Direct port of the macOS SessionScanner; the
/// thresholds are the tuned values from the shipping app.
public static class SessionScanner
{
    private static readonly TimeSpan ActiveWindow = TimeSpan.FromSeconds(18);
    private static readonly TimeSpan StallAfter = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan StallCap = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan NeedsYouCap = TimeSpan.FromMinutes(20);
    private static readonly TimeSpan AttentionWindow = TimeSpan.FromMinutes(30);
    /// Claude Desktop writes lastActivityAt 2.3-4.2s after the final assistant
    /// event as turn-completion bookkeeping; external activity inside this
    /// window must not suppress needsYou.
    private static readonly TimeSpan DesktopBookkeepingGrace = TimeSpan.FromSeconds(25);

    private const int MonitoringCodexLimit = 120;

    // MARK: - Entry points

    /// Picker scan: desktop-titled Claude threads (archived filtered) UNION
    /// transcript-only CLI threads the desktop store has never seen, plus
    /// one Codex entry per project.
    public static List<ScannedSession> Scan(DateTimeOffset now, IReadOnlyDictionary<string, DateTimeOffset> lastWorking)
    {
        var output = ScanClaudeFromDesktopStore(now, lastWorking);
        var known = new HashSet<string>(output.Select(s => s.SessionId), StringComparer.Ordinal);
        output.AddRange(ScanClaudeTranscripts(now, lastWorking, excludeArchived: true)
            .Where(s => !known.Contains(s.SessionId)));
        output.AddRange(ScanCodex(now, lastWorking, limit: 30, dedupeProjects: true));
        output.Sort((a, b) => b.Modified.CompareTo(a.Modified));
        return output;
    }

    /// Monitoring scan: every Claude transcript (desktop-labelled when known)
    /// + every recent Codex rollout, no project dedupe. Subagent threads only
    /// participate when the user opted into subagent alarms.
    public static List<ScannedSession> MonitoringScan(DateTimeOffset now, IReadOnlyDictionary<string, DateTimeOffset> lastWorking)
    {
        var includeSubagents = Alarm.SubagentAlarmStore.Shared.Enabled;
        var output = ScanClaudeTranscripts(now, lastWorking, excludeArchived: false, includeSubagents);
        output.AddRange(ScanCodex(
            now, lastWorking, limit: MonitoringCodexLimit, dedupeProjects: false, includeSubagents));
        output.Sort((a, b) => b.Modified.CompareTo(a.Modified));
        return output;
    }

    // MARK: - Claude: desktop session store (titles + archived flag)

    private static List<ScannedSession> ScanClaudeFromDesktopStore(
        DateTimeOffset now,
        IReadOnlyDictionary<string, DateTimeOffset> lastWorking)
    {
        var output = new List<ScannedSession>();
        var transcripts = ClaudeTranscriptIndex();
        foreach (var entry in EnumerateDesktopSessionFiles())
        {
            var parsed = ParseDesktopSessionFile(entry);
            if (parsed is not { } session) continue;
            if (session.IsArchived) continue;
            if (string.IsNullOrEmpty(session.CliSessionId)) continue;
            transcripts.TryGetValue(session.CliSessionId, out var transcript);
            var state = SessionState(
                transcript,
                now,
                lastWorking,
                session.LastActivityAt,
                SessionTurnState.Claude);
            output.Add(new ScannedSession(
                TriggerTool.Claude,
                session.CliSessionId,
                session.Cwd,
                string.IsNullOrEmpty(session.Title) ? Fallback(session.Cwd, session.CliSessionId) : session.Title,
                state.Modified,
                state.Status,
                transcript,
                state.TurnKey,
                SessionLaunchTarget.ClaudeDesktop));
        }
        return output;
    }

    // MARK: - Claude: all transcripts (monitoring)

    private static List<ScannedSession> ScanClaudeTranscripts(
        DateTimeOffset now,
        IReadOnlyDictionary<string, DateTimeOffset> lastWorking,
        bool excludeArchived = false,
        bool includeSubagents = false)
    {
        var desktopSessions = ClaudeDesktopIndex();
        var output = new List<ScannedSession>();
        foreach (var (sid, path) in ClaudeTranscriptIndex(includeSubagents))
        {
            if (IsClaudeSubagentTranscript(path))
            {
                if (AgentSession(path, now, lastWorking) is { } agent) output.Add(agent);
                continue;
            }
            desktopSessions.TryGetValue(sid, out var desktop);
            if (excludeArchived && desktop is { IsArchived: true }) continue;
            var cwd = desktop?.Cwd is { Length: > 0 } dc ? dc : CwdFromClaudeTranscript(path);
            var title = desktop?.Title ?? "";
            var state = SessionState(
                path,
                now,
                lastWorking,
                desktop?.LastActivityAt,
                SessionTurnState.Claude);
            output.Add(new ScannedSession(
                TriggerTool.Claude,
                sid,
                cwd,
                string.IsNullOrEmpty(title) ? Fallback(cwd, sid) : title,
                state.Modified,
                state.Status,
                path,
                state.TurnKey,
                desktop is null ? SessionLaunchTarget.Cli : SessionLaunchTarget.ClaudeDesktop));
        }
        return output;
    }

    /// A subagent transcript scanned as an alarm source (opt-in). The thread
    /// resumes through its PARENT session — `claude --resume` only accepts
    /// real session ids, and every subagent line carries the parent's.
    private static ScannedSession? AgentSession(
        string path,
        DateTimeOffset now,
        IReadOnlyDictionary<string, DateTimeOffset> lastWorking)
    {
        var (parentSid, cwd) = ClaudeAgentMeta(path);
        if (string.IsNullOrEmpty(parentSid)) return null;
        var state = SessionState(path, now, lastWorking, null, SessionTurnState.ClaudeAgent);
        var label = ClaudeAgentLabel(path);
        return new ScannedSession(
            TriggerTool.Claude,
            parentSid,
            cwd,
            string.IsNullOrEmpty(label) ? Fallback(cwd, parentSid) : label,
            state.Modified,
            state.Status,
            path,
            state.TurnKey,
            SessionLaunchTarget.Cli);
    }

    /// Parent session id + cwd from the transcript's own lines; falls back to
    /// the nested layout's directory name (<parent-session>/subagents/…).
    private static (string ParentSid, string Cwd) ClaudeAgentMeta(string path)
    {
        try
        {
            using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            for (var i = 0; i < 30 && reader.ReadLine() is { } line; i++)
            {
                using var doc = Jsonl.TryParseLine(line);
                if (doc is null) continue;
                var sid = Jsonl.GetString(doc.RootElement, "sessionId");
                if (!string.IsNullOrEmpty(sid))
                {
                    return (sid!, Jsonl.GetString(doc.RootElement, "cwd") ?? "");
                }
            }
        }
        catch
        {
        }
        var dir = Path.GetDirectoryName(path);
        if (string.Equals(Path.GetFileName(dir), "subagents", StringComparison.OrdinalIgnoreCase)
            && Path.GetDirectoryName(dir) is { } parentDir)
        {
            return (Path.GetFileName(parentDir), "");
        }
        return ("", "");
    }

    /// The sibling agent-<id>.meta.json carries the Task tool's one-line
    /// description — the best available alarm title for a subagent.
    private static string ClaudeAgentLabel(string path)
    {
        try
        {
            var metaPath = Path.ChangeExtension(path, ".meta.json");
            if (!File.Exists(metaPath)) return "";
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(metaPath));
            return Jsonl.GetString(doc.RootElement, "description") ?? "";
        }
        catch
        {
            return "";
        }
    }

    /// True when a transcript belongs to an orchestrated subagent rather
    /// than a user conversation: nested under a subagents/ directory (the
    /// current layout) or named agent-*.jsonl (flat layouts). Main session
    /// files are always UUID-named.
    internal static bool IsClaudeSubagentTranscript(string path)
    {
        var segments = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (segments.Contains("subagents", StringComparer.OrdinalIgnoreCase)) return true;
        return segments[^1].StartsWith("agent-", StringComparison.OrdinalIgnoreCase);
    }

    // MARK: - Codex: ~/.codex/sessions

    public static List<ScannedSession> ScanCodex(
        DateTimeOffset now,
        IReadOnlyDictionary<string, DateTimeOffset> lastWorking,
        int limit = 30,
        bool dedupeProjects = true,
        bool includeSubagents = false)
    {
        var root = IslandPaths.CodexSessionsRoot;
        if (!Directory.Exists(root)) return new List<ScannedSession>();

        var files = SafeEnumerateFiles(root, "*.jsonl");
        // Precompute mtimes once — calling Mtime() inside the comparator issues
        // O(n log n) GetLastWriteTime syscalls over a full recursive tree.
        var mtimes = new Dictionary<string, DateTimeOffset>(files.Count, StringComparer.Ordinal);
        foreach (var f in files) mtimes[f] = Mtime(f);
        files.Sort((a, b) => mtimes[b].CompareTo(mtimes[a]));

        var titles = CodexTitleIndex();
        var output = new List<ScannedSession>();
        var seenProjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in files)
        {
            if (CodexMeta(path) is not var (sid, cwd, kind) || string.IsNullOrEmpty(sid)) continue;
            if (kind == CodexRolloutKind.Automation) continue;
            if (kind == CodexRolloutKind.Subagent && !includeSubagents) continue;
            var projectKey = string.IsNullOrEmpty(cwd) ? sid : cwd;
            if (dedupeProjects && !seenProjects.Add(projectKey)) continue;
            var state = SessionState(path, now, lastWorking, null, SessionTurnState.Codex);
            output.Add(new ScannedSession(
                TriggerTool.Codex,
                sid,
                cwd,
                titles.TryGetValue(sid, out var title) ? title : Fallback(cwd, sid),
                state.Modified,
                state.Status,
                path,
                state.TurnKey,
                SessionLaunchTarget.Cli));
            if (output.Count >= limit) break;
        }
        return output;
    }

    internal enum CodexRolloutKind
    {
        Interactive,
        Subagent,
        Automation,
    }

    /// Reads the first JSONL line in full. Codex's `session_meta` is line 1
    /// but can be tens of KB (it embeds the full base instructions), so keep
    /// pulling chunks until the first newline.
    private static (string Sid, string Cwd, CodexRolloutKind Kind)? CodexMeta(string path)
    {
        byte[] firstLine;
        try
        {
            using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            var buffer = new List<byte>(65_536);
            var chunk = new byte[65_536];
            var newline = -1;
            while (newline < 0)
            {
                var read = stream.Read(chunk, 0, chunk.Length);
                if (read <= 0) break;
                for (var i = 0; i < read && newline < 0; i++)
                {
                    if (chunk[i] == (byte)'\n') newline = buffer.Count + i;
                }
                buffer.AddRange(chunk.AsSpan(0, read).ToArray());
                if (buffer.Count > 2_000_000) break;
            }
            firstLine = newline >= 0 ? buffer.Take(newline).ToArray() : buffer.ToArray();
        }
        catch
        {
            return null;
        }
        return ParseCodexMeta(Encoding.UTF8.GetString(firstLine));
    }

    /// Classifies a rollout from its session_meta line. `payload.source` is
    /// a plain string for direct sessions ("cli", "vscode", "exec", "mcp")
    /// and an object for machine-driven ones: {"subagent": …} for spawned /
    /// review / compact threads, {"internal": …} for probes. Subagent
    /// threads share the interactive codex originator, so the source field
    /// is the only thing separating a fan-out worker from the user's own
    /// thread — without it every finished subagent raises a turn alarm.
    internal static (string Sid, string Cwd, CodexRolloutKind Kind)? ParseCodexMeta(string firstLine)
    {
        using var doc = Jsonl.TryParseLine(firstLine);
        if (doc is null) return null;
        var root = doc.RootElement;
        if (Jsonl.GetString(root, "type") != "session_meta") return null;
        if (Jsonl.GetObject(root, "payload") is not { } payload) return null;

        var kind = CodexRolloutKind.Interactive;
        if (payload.TryGetProperty("source", out var source))
        {
            if (source.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                if (source.GetString() is "exec" or "mcp") kind = CodexRolloutKind.Automation;
            }
            else if (source.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                if (source.TryGetProperty("subagent", out _)) kind = CodexRolloutKind.Subagent;
                else if (source.TryGetProperty("internal", out _)) kind = CodexRolloutKind.Automation;
            }
        }

        // Automation rollouts (orchestrator-driven runs, probes, and
        // `codex exec`) finish constantly; a human is never "up" in them,
        // so they must not raise turn alarms or drive the logo. Interactive
        // sessions carry a codex-family originator; missing originator =
        // old CLI, treat as interactive. The prefix check is
        // case-insensitive: the Windows desktop app stamps "Codex Desktop".
        var originator = Jsonl.GetString(payload, "originator") ?? "";
        if (originator.Length > 0
            && (!originator.StartsWith("codex", StringComparison.OrdinalIgnoreCase)
                || string.Equals(originator, "codex_exec", StringComparison.OrdinalIgnoreCase)))
        {
            kind = CodexRolloutKind.Automation;
        }

        return (Jsonl.GetString(payload, "id") ?? "", Jsonl.GetString(payload, "cwd") ?? "", kind);
    }

    // MARK: - Indexes

    public static Dictionary<string, string> ClaudeTranscriptIndex(bool includeSubagents = false)
    {
        var output = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var root in IslandPaths.ClaudeProjectRoots)
        {
            if (!Directory.Exists(root)) continue;
            foreach (var path in SafeEnumerateFiles(root, "*.jsonl"))
            {
                // Subagent transcripts (subagents/ dirs, agent-*.jsonl) are
                // machine fan-out, not user conversations; they only enter
                // the monitoring scan when subagent alarms are opted in.
                if (!includeSubagents && IsClaudeSubagentTranscript(Path.GetRelativePath(root, path)))
                {
                    continue;
                }
                var sid = Path.GetFileNameWithoutExtension(path);
                output.TryAdd(sid, path);
            }
        }
        return output;
    }

    private sealed record DesktopSession(
        string CliSessionId, string Title, string Cwd, bool IsArchived, DateTimeOffset? LastActivityAt);

    private static IEnumerable<string> EnumerateDesktopSessionFiles()
    {
        var root = IslandPaths.ClaudeDesktopSessionsRoot;
        if (!Directory.Exists(root)) return Array.Empty<string>();
        return SafeEnumerateFiles(root, "local_*.json");
    }

    /// Recursively lists matching files, surviving a subdirectory that is
    /// inaccessible OR deleted mid-walk (Claude Code rotates project folders).
    /// A plain EnumerateFiles(AllDirectories) throws DURING iteration — outside
    /// any try around the call — which used to fault the whole scan and, with
    /// it, every turn alarm. Skips reparse points to avoid symlink loops.
    internal static List<string> SafeEnumerateFiles(string root, string pattern)
    {
        var result = new List<string>();
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };
        try
        {
            using var walker = Directory.EnumerateFiles(root, pattern, options).GetEnumerator();
            while (true)
            {
                try
                {
                    if (!walker.MoveNext()) break;
                }
                catch
                {
                    // A directory vanished or turned unreadable mid-walk —
                    // stop cleanly and keep what we already gathered.
                    break;
                }
                result.Add(walker.Current);
            }
        }
        catch
        {
        }
        return result;
    }

    private static DesktopSession? ParseDesktopSessionFile(string path)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(path));
            var root = doc.RootElement;
            var cliSessionId = Jsonl.GetString(root, "cliSessionId") ?? "";
            var ms = Jsonl.GetDouble(root, "lastActivityAt") ?? Jsonl.GetDouble(root, "createdAt");
            return new DesktopSession(
                cliSessionId,
                Jsonl.GetString(root, "title") ?? "",
                Jsonl.GetString(root, "cwd") ?? "",
                Jsonl.GetBool(root, "isArchived") ?? false,
                ms is { } m ? DateTimeOffset.FromUnixTimeMilliseconds((long)m) : null);
        }
        catch
        {
            return null;
        }
    }

    private static Dictionary<string, DesktopSession> ClaudeDesktopIndex()
    {
        var output = new Dictionary<string, DesktopSession>(StringComparer.Ordinal);
        foreach (var path in EnumerateDesktopSessionFiles())
        {
            if (ParseDesktopSessionFile(path) is not { } session) continue;
            if (string.IsNullOrEmpty(session.CliSessionId)) continue;
            output[session.CliSessionId] = session;
        }
        return output;
    }

    private static Dictionary<string, string> CodexTitleIndex()
    {
        var output = new Dictionary<string, string>(StringComparer.Ordinal);
        var path = IslandPaths.CodexSessionIndexFile;
        if (!File.Exists(path)) return output;
        string[] lines;
        try
        {
            lines = File.ReadAllLines(path, Encoding.UTF8);
        }
        catch
        {
            return output;
        }
        foreach (var line in lines)
        {
            using var doc = Jsonl.TryParseLine(line);
            if (doc is null) continue;
            var id = Jsonl.GetString(doc.RootElement, "id");
            var title = Jsonl.GetString(doc.RootElement, "thread_name")?.Trim();
            if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(title)) output[id!] = title!;
        }
        return output;
    }

    // MARK: - State machine

    public static (ActivityState Status, string? TurnKey, DateTimeOffset Modified) SessionState(
        string? path,
        DateTimeOffset now,
        IReadOnlyDictionary<string, DateTimeOffset> lastWorking,
        DateTimeOffset? externalActivityDate,
        Func<IReadOnlyList<string>, SessionTurnStatus> turnState)
    {
        if (path is null)
            return (ActivityState.Idle, null, externalActivityDate ?? DateTimeOffset.MinValue);

        var fileModified = Mtime(path);
        var lines = TailLines(path);
        var turn = turnState(lines);
        var semanticModified = LatestDate(turn.ActivityDate, externalActivityDate);
        var effectiveModified = semanticModified ?? fileModified;
        // For a finished turn, external activity inside the bookkeeping grace
        // is Claude Desktop's own post-turn write — not the user returning —
        // so it must not suppress needsYou. Genuine "user came back" activity
        // lands minutes later, far past the grace.
        var externalReference = turn.IsDone
            ? turn.ActivityDate?.Add(DesktopBookkeepingGrace)
            : turn.ActivityDate;
        var externalIsNewer = IsLater(externalActivityDate, externalReference);
        var age = now - effectiveModified;

        if (age > AttentionWindow) return (ActivityState.Idle, turn.Key, effectiveModified);
        if (externalIsNewer)
        {
            if (age < StallAfter) return (ActivityState.Working, turn.Key, effectiveModified);
            if (lastWorking.TryGetValue(path, out var seen)
                && now - seen < StallCap
                && age < StallCap)
            {
                return (ActivityState.Stalled, turn.Key, effectiveModified);
            }
            return (ActivityState.Idle, turn.Key, effectiveModified);
        }
        if (turn.IsDone)
            return (age < NeedsYouCap ? ActivityState.NeedsYou : ActivityState.Idle, turn.Key, effectiveModified);
        if (age < ActiveWindow) return (ActivityState.Working, turn.Key, effectiveModified);
        if (age < StallAfter) return (ActivityState.Working, turn.Key, effectiveModified);
        if (lastWorking.TryGetValue(path, out var seenAgain)
            && now - seenAgain < StallCap
            && age < StallCap)
        {
            return (ActivityState.Stalled, turn.Key, effectiveModified);
        }
        return (ActivityState.Idle, turn.Key, effectiveModified);
    }

    // MARK: - Helpers

    public static string Fallback(string cwd, string sid)
    {
        var trimmed = cwd.TrimEnd('\\', '/');
        var basename = trimmed.Length == 0 ? "" : Path.GetFileName(trimmed);
        if (string.IsNullOrEmpty(basename))
            return sid.Length <= 8 ? sid : sid[..8];
        return basename;
    }

    /// The transcript itself records the true working directory on nearly
    /// every entry ("cwd") — authoritative, unlike the lossy encoded folder
    /// name, whose dashes un-munge real hyphenated paths into the wrong
    /// directory and then break `claude --resume` launched from it.
    internal static string CwdFromClaudeTranscript(string path)
    {
        try
        {
            using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            for (var i = 0; i < 30 && reader.ReadLine() is { } line; i++)
            {
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(line);
                    if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
                        && doc.RootElement.TryGetProperty("cwd", out var cwd)
                        && cwd.ValueKind == System.Text.Json.JsonValueKind.String
                        && cwd.GetString() is { Length: > 0 } value)
                    {
                        return value;
                    }
                }
                catch (System.Text.Json.JsonException)
                {
                }
            }
        }
        catch
        {
        }
        return ProjectFromClaudeTranscript(path);
    }

    /// Display-only fallback when the transcript carries no cwd: reverse
    /// the encoded project directory name. The encoding is lossy (path
    /// separators and ':' both became '-'), so this is a best-effort label,
    /// same as on macOS.
    private static string ProjectFromClaudeTranscript(string path)
    {
        var parent = Path.GetFileName(Path.GetDirectoryName(path) ?? "");
        if (parent.Length == 0) return "";
        // Windows transcripts encode e.g. C:\Users\me\proj as C--Users-me-proj.
        if (parent.Length > 3 && char.IsAsciiLetter(parent[0]) && parent[1] == '-' && parent[2] == '-')
        {
            return parent[0] + ":\\" + parent[3..].Replace('-', '\\');
        }
        return "/" + parent.Replace('-', '/');
    }

    public static List<string> TailLines(string path, long bytes = 131_072, int keep = 200)
    {
        byte[] data;
        try
        {
            using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            var size = stream.Length;
            if (size > bytes) stream.Seek(size - bytes, SeekOrigin.Begin);
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            data = memory.ToArray();
        }
        catch
        {
            return new List<string>();
        }
        var text = Encoding.UTF8.GetString(data);
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var start = Math.Max(0, lines.Length - keep);
        var output = new List<string>(Math.Min(keep, lines.Length));
        for (var i = start; i < lines.Length; i++)
        {
            output.Add(lines[i].TrimEnd('\r'));
        }
        return output;
    }

    public static DateTimeOffset Mtime(string path)
    {
        try
        {
            var utc = File.GetLastWriteTimeUtc(path);
            return utc.Year < 1700 ? DateTimeOffset.MinValue : new DateTimeOffset(utc);
        }
        catch
        {
            return DateTimeOffset.MinValue;
        }
    }

    private static DateTimeOffset? LatestDate(DateTimeOffset? lhs, DateTimeOffset? rhs)
    {
        if (lhs is { } l && rhs is { } r) return l > r ? l : r;
        return lhs ?? rhs;
    }

    private static bool IsLater(DateTimeOffset? lhs, DateTimeOffset? rhs)
    {
        if (lhs is not { } l) return false;
        if (rhs is not { } r) return true;
        return (l - r).TotalSeconds > 0.5;
    }
}

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
    /// + every recent Codex rollout, no project dedupe.
    public static List<ScannedSession> MonitoringScan(DateTimeOffset now, IReadOnlyDictionary<string, DateTimeOffset> lastWorking)
    {
        var output = ScanClaudeTranscripts(now, lastWorking);
        output.AddRange(ScanCodex(now, lastWorking, limit: MonitoringCodexLimit, dedupeProjects: false));
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
        bool excludeArchived = false)
    {
        var desktopSessions = ClaudeDesktopIndex();
        var output = new List<ScannedSession>();
        foreach (var (sid, path) in ClaudeTranscriptIndex())
        {
            desktopSessions.TryGetValue(sid, out var desktop);
            if (excludeArchived && desktop is { IsArchived: true }) continue;
            var cwd = desktop?.Cwd is { Length: > 0 } dc ? dc : ProjectFromClaudeTranscript(path);
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

    // MARK: - Codex: ~/.codex/sessions

    public static List<ScannedSession> ScanCodex(
        DateTimeOffset now,
        IReadOnlyDictionary<string, DateTimeOffset> lastWorking,
        int limit = 30,
        bool dedupeProjects = true)
    {
        var root = IslandPaths.CodexSessionsRoot;
        if (!Directory.Exists(root)) return new List<ScannedSession>();

        List<string> files;
        try
        {
            files = Directory.EnumerateFiles(root, "*.jsonl", SearchOption.AllDirectories).ToList();
        }
        catch
        {
            return new List<ScannedSession>();
        }
        files.Sort((a, b) => Mtime(b).CompareTo(Mtime(a)));

        var titles = CodexTitleIndex();
        var output = new List<ScannedSession>();
        var seenProjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in files)
        {
            if (CodexMeta(path) is not var (sid, cwd) || string.IsNullOrEmpty(sid)) continue;
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

    /// Reads the first JSONL line in full. Codex's `session_meta` is line 1
    /// but can be tens of KB (it embeds the full base instructions), so keep
    /// pulling chunks until the first newline.
    private static (string Sid, string Cwd)? CodexMeta(string path)
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

        using var doc = Jsonl.TryParseLine(Encoding.UTF8.GetString(firstLine));
        if (doc is null) return null;
        var root = doc.RootElement;
        if (Jsonl.GetString(root, "type") != "session_meta") return null;
        if (Jsonl.GetObject(root, "payload") is not { } payload) return null;

        // Automation rollouts (orchestrator-spawned subagents, probes, and
        // `codex exec` runs) finish constantly; a human is never "up" in
        // them, so they must not raise turn alarms or drive the logo.
        // Interactive sessions carry a codex-family originator; missing
        // originator = old CLI, treat as interactive. The prefix check is
        // case-insensitive: the Windows desktop app stamps "Codex Desktop".
        var originator = Jsonl.GetString(payload, "originator") ?? "";
        if (originator.Length > 0
            && (!originator.StartsWith("codex", StringComparison.OrdinalIgnoreCase)
                || string.Equals(originator, "codex_exec", StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }
        return (Jsonl.GetString(payload, "id") ?? "", Jsonl.GetString(payload, "cwd") ?? "");
    }

    // MARK: - Indexes

    public static Dictionary<string, string> ClaudeTranscriptIndex()
    {
        var output = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var root in IslandPaths.ClaudeProjectRoots)
        {
            if (!Directory.Exists(root)) continue;
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(root, "*.jsonl", SearchOption.AllDirectories);
            }
            catch
            {
                continue;
            }
            foreach (var path in files)
            {
                // Subagent transcripts live under <project>/subagents/ and must
                // not drive alarms or the logo.
                var relative = Path.GetRelativePath(root, path);
                if (relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Contains("subagents", StringComparer.OrdinalIgnoreCase))
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
        if (!Directory.Exists(root)) yield break;
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(root, "local_*.json", SearchOption.AllDirectories);
        }
        catch
        {
            yield break;
        }
        foreach (var file in files) yield return file;
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

    /// Display-only fallback when the desktop store has no cwd for a
    /// transcript: reverse the encoded project directory name. The encoding
    /// is lossy (path separators and ':' both became '-'), so this is a
    /// best-effort label, same as on macOS.
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

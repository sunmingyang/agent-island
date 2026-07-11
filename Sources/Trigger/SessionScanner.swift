import Foundation

/// A resumable session discovered on disk, for the trigger picker.
struct ScannedSession: Identifiable, Hashable {
    var id: String { tool.rawValue + ":" + sessionId }
    let tool: TriggerTool
    let sessionId: String   // the id passed to `--resume` / `exec resume`
    let cwd: String
    let label: String       // clean display name
    let modified: Date
    let status: ActivityMonitor.State
    let transcriptPath: String?
    let turnKey: String?
    let launchTarget: SessionLaunchTarget
}

enum SessionLaunchTarget: Hashable {
    case cli
    case claudeDesktop
}

enum SessionScanner {
    private static let activeWindow: TimeInterval = 18
    private static let stallAfter: TimeInterval = 5 * 60
    private static let stallCap: TimeInterval = 15 * 60
    private static let needsYouCap: TimeInterval = 20 * 60
    private static let attentionWindow: TimeInterval = 30 * 60
    private static let desktopBookkeepingGrace: TimeInterval = 25

    static func scan(now: Date = Date(), lastWorking: [String: Date] = [:]) -> [ScannedSession] {
        var out = scanClaude(now: now, lastWorking: lastWorking)
        out += scanCodex(now: now, lastWorking: lastWorking)
        out.sort { $0.modified > $1.modified }
        // Dedupe by session: the Claude desktop store commonly holds the SAME
        // cliSessionId under two project folders (23 of 41 on the reporting
        // machine), so a raw file scan lists every such session twice in the
        // trigger picker. Sorted newest-first, keep the first sighting of each
        // (tool, sessionId).
        var seen = Set<String>()
        out = out.filter { seen.insert("\($0.tool.rawValue):\($0.sessionId)").inserted }
        return out
    }

    // MARK: - Claude: desktop session store (titles + archived flag)

    private static func scanClaude(now: Date, lastWorking: [String: Date]) -> [ScannedSession] {
        let fm = FileManager.default
        let root = NSHomeDirectory() + "/Library/Application Support/Claude/claude-code-sessions"
        guard let enumerator = fm.enumerator(atPath: root) else { return [] }
        let transcripts = claudeTranscriptIndex()
        var out: [ScannedSession] = []
        for case let rel as String in enumerator
        where rel.hasSuffix(".json") && (rel as NSString).lastPathComponent.hasPrefix("local_") {
            let path = root + "/" + rel
            guard let data = fm.contents(atPath: path),
                  let object = try? JSONSerialization.jsonObject(with: data) as? [String: Any]
            else { continue }
            // Skip archived threads — the picker only lists active ones.
            if object["isArchived"] as? Bool == true { continue }
            guard let resume = object["cliSessionId"] as? String, !resume.isEmpty else { continue }
            let cwd = object["cwd"] as? String ?? ""
            let title = object["title"] as? String ?? ""
            let ms = (object["lastActivityAt"] as? Double) ?? (object["createdAt"] as? Double) ?? 0
            let desktopActivity = Date(timeIntervalSince1970: ms / 1000)
            let transcript = transcripts[resume]
            let state = sessionState(
                for: transcript,
                now: now,
                lastWorking: lastWorking,
                externalActivityDate: desktopActivity,
                turnState: SessionTurnState.claude
            )
            out.append(ScannedSession(
                tool: .claude,
                sessionId: resume,
                cwd: cwd,
                label: title.isEmpty ? fallback(cwd, resume) : title,
                modified: state.modified,
                status: state.status,
                transcriptPath: transcript,
                turnKey: state.turnKey,
                launchTarget: .claudeDesktop
            ))
        }
        return out
    }

    // MARK: - Codex: ~/.codex/sessions, one entry per project folder

    static func scanCodex(
        now: Date,
        lastWorking: [String: Date],
        limit: Int = 30,
        dedupeProjects: Bool = true
    ) -> [ScannedSession] {
        let fm = FileManager.default
        let root = NSHomeDirectory() + "/.codex/sessions"
        guard let enumerator = fm.enumerator(atPath: root) else { return [] }
        let titles = codexTitleIndex()
        var files: [String] = []
        for case let rel as String in enumerator where rel.hasSuffix(".jsonl") {
            files.append(root + "/" + rel)
        }
        // Stat each file ONCE, then sort by the cached mtime. Calling mtime()
        // inside the comparator re-stats every file O(n log n) times — the
        // dominant cost of the every-few-seconds monitoring scan.
        files = files
            .map { (path: $0, modified: mtime($0)) }
            .sorted { $0.modified > $1.modified }
            .map(\.path)
        var out: [ScannedSession] = []
        var seenProjects = Set<String>()
        for path in files {
            guard let (sid, cwd) = codexMeta(path), !sid.isEmpty else { continue }
            let projectKey = cwd.isEmpty ? sid : cwd
            if dedupeProjects {
                if seenProjects.contains(projectKey) { continue }
                seenProjects.insert(projectKey)
            }
            let state = sessionState(for: path, now: now, lastWorking: lastWorking, turnState: SessionTurnState.codex)
            out.append(ScannedSession(
                tool: .codex,
                sessionId: sid,
                cwd: cwd,
                label: titles[sid] ?? fallback(cwd, sid),
                modified: state.modified,
                status: state.status,
                transcriptPath: path,
                turnKey: state.turnKey,
                launchTarget: .cli
            ))
            if out.count >= limit { break }
        }
        return out
    }

    /// Reads the first JSONL line in full. Codex's `session_meta` is line 1 but
    /// can be tens of KB (it embeds the full base instructions), so a fixed-size
    /// read truncates it — keep pulling chunks until the first newline.
    private static func codexMeta(_ path: String) -> (String, String)? {
        guard let handle = FileHandle(forReadingAtPath: path) else { return nil }
        defer { try? handle.close() }
        var buffer = Data()
        while buffer.firstIndex(of: 0x0A) == nil {
            guard let chunk = try? handle.read(upToCount: 65_536), !chunk.isEmpty else { break }
            buffer.append(chunk)
            if buffer.count > 2_000_000 { break }
        }
        let firstLine = buffer.firstIndex(of: 0x0A).map { buffer.prefix(upTo: $0) } ?? buffer.prefix(buffer.count)
        guard let object = try? JSONSerialization.jsonObject(with: Data(firstLine)) as? [String: Any],
              object["type"] as? String == "session_meta",
              let payload = object["payload"] as? [String: Any]
        else { return nil }
        // Two tiers of machine-driven sessions, told apart on session_meta
        // (originator can't separate them — a spawned subagent carries the SAME
        // "Codex Desktop" originator as an interactive session):
        //
        // 1. AUTOMATION — never surfaced, no opt-in: `codex exec` runs, probes,
        //    bridges (originator substrings), source == "exec"/"mcp" strings,
        //    and {"internal": …} probe objects. A human is never "up" in these.
        let originator = (payload["originator"] as? String ?? "").lowercased()
        if originator.contains("exec") || originator.contains("probe") || originator.contains("bridge") {
            return nil
        }
        if let source = payload["source"] as? String, source == "exec" || source == "mcp" {
            return nil
        }
        if let source = payload["source"] as? [String: Any], source["internal"] != nil {
            return nil
        }
        // 2. SUBAGENT / child threads (orchestrator fan-out: spawned/review/
        //    compact) — filtered by DEFAULT because they finish constantly, but
        //    the user can opt back in via SubagentAlarmStore ("Alarm on subagent
        //    threads"). All three spawn markers must honor the toggle, or an
        //    enabled toggle would still be dead: thread_source == "subagent",
        //    a non-empty parent_thread_id, and a {"subagent": …} source object
        //    co-occur on the same rollouts. UserDefaults is thread-safe; this
        //    runs off the main actor.
        let isSubagent = (payload["thread_source"] as? String) == "subagent"
            || (payload["parent_thread_id"] as? String).map({ !$0.isEmpty }) == true
            || (payload["source"] as? [String: Any])?["subagent"] != nil
        if isSubagent, !UserDefaults.standard.bool(forKey: subagentAlarmDefaultsKey) {
            return nil
        }
        return (payload["id"] as? String ?? "", payload["cwd"] as? String ?? "")
    }

    // MARK: - Helpers

    static func fallback(_ cwd: String, _ sid: String) -> String {
        let base = (cwd as NSString).lastPathComponent
        return base.isEmpty ? String(sid.prefix(8)) : base
    }

    private static func codexTitleIndex() -> [String: String] {
        let path = NSHomeDirectory() + "/.codex/session_index.jsonl"
        guard let text = try? String(contentsOfFile: path, encoding: .utf8) else { return [:] }
        var out: [String: String] = [:]
        for line in text.split(separator: "\n") {
            guard let object = json(String(line)),
                  let id = object["id"] as? String,
                  let title = object["thread_name"] as? String
            else { continue }
            let clean = title.trimmingCharacters(in: .whitespacesAndNewlines)
            if !id.isEmpty && !clean.isEmpty { out[id] = clean }
        }
        return out
    }

    static func claudeTranscriptIndex() -> [String: String] {
        let root = NSHomeDirectory() + "/.claude/projects"
        guard let enumerator = FileManager.default.enumerator(atPath: root) else { return [:] }
        var out: [String: String] = [:]
        for case let rel as String in enumerator where rel.hasSuffix(".jsonl") {
            // Subagent transcripts: subagents/ dirs (current layout) or
            // agent-*.jsonl names (flat layouts). Main sessions are always
            // UUID-named. Machine fan-out must not drive alarms or the logo.
            if rel.contains("/subagents/") { continue }
            if ((rel as NSString).lastPathComponent).hasPrefix("agent-") { continue }
            let path = root + "/" + rel
            let sid = ((rel as NSString).lastPathComponent as NSString).deletingPathExtension
            out[sid] = path
        }
        return out
    }

    static func status(
        for path: String?,
        now: Date,
        lastWorking: [String: Date],
        turnDone: ([String]) -> Bool
    ) -> ActivityMonitor.State {
        sessionState(
            for: path,
            now: now,
            lastWorking: lastWorking,
            turnState: { lines in SessionTurnStatus(isDone: turnDone(lines), key: nil, activityDate: nil) }
        ).status
    }

    static func sessionState(
        for path: String?,
        now: Date,
        lastWorking: [String: Date],
        externalActivityDate: Date? = nil,
        turnState: ([String]) -> SessionTurnStatus
    ) -> (status: ActivityMonitor.State, turnKey: String?, modified: Date) {
        guard let path else {
            return (.idle, nil, externalActivityDate ?? .distantPast)
        }
        let fileModified = mtime(path)
        let lines = tailLines(path)
        let turn = turnState(lines)
        let semanticModified = latestDate(turn.activityDate, externalActivityDate)
        let effectiveModified = semanticModified ?? fileModified
        // Claude Desktop writes lastActivityAt a few seconds AFTER the final
        // assistant event as turn-completion bookkeeping (measured 2.3-4.2s on
        // real threads). For a finished turn, external activity inside that
        // window is the bookkeeping write — not the user returning — so it
        // must not suppress needsYou, or the alarm only fires when the 6s scan
        // tick happens to land inside the gap. Genuine "user came back"
        // activity lands minutes later, far past the grace.
        let externalReference = turn.isDone
            ? turn.activityDate.map { $0.addingTimeInterval(desktopBookkeepingGrace) }
            : turn.activityDate
        let externalIsNewer = isLater(externalActivityDate, than: externalReference)
        let age = now.timeIntervalSince(effectiveModified)
        if age > attentionWindow { return (.idle, turn.key, effectiveModified) }
        if externalIsNewer {
            if age < stallAfter { return (.working, turn.key, effectiveModified) }
            if let seen = lastWorking[path],
               now.timeIntervalSince(seen) < stallCap,
               age < stallCap {
                return (.stalled, turn.key, effectiveModified)
            }
            return (.idle, turn.key, effectiveModified)
        }
        if turn.isDone { return (age < needsYouCap ? .needsYou : .idle, turn.key, effectiveModified) }
        if age < activeWindow { return (.working, turn.key, effectiveModified) }
        if age < stallAfter { return (.working, turn.key, effectiveModified) }
        if let seen = lastWorking[path],
           now.timeIntervalSince(seen) < stallCap,
           age < stallCap {
            return (.stalled, turn.key, effectiveModified)
        }
        return (.idle, turn.key, effectiveModified)
    }

    static func claudeTurnDone(_ lines: [String]) -> Bool {
        SessionTurnState.claude(lines).isDone
    }

    private static func codexTurnDone(_ lines: [String]) -> Bool {
        SessionTurnState.codex(lines).isDone
    }

    private static func tailLines(_ path: String, bytes: UInt64 = 131_072, keep: Int = 200) -> [String] {
        guard let handle = FileHandle(forReadingAtPath: path) else { return [] }
        defer { try? handle.close() }
        let size = (try? handle.seekToEnd()) ?? 0
        try? handle.seek(toOffset: size > bytes ? size - bytes : 0)
        let data = (try? handle.readToEnd()) ?? Data()
        return Array(String(decoding: data, as: UTF8.self).split(separator: "\n").map(String.init).suffix(keep))
    }

    private static func json(_ line: String) -> [String: Any]? {
        guard let data = line.data(using: .utf8) else { return nil }
        return (try? JSONSerialization.jsonObject(with: data)) as? [String: Any]
    }

    /// Modification time via a single `stat(2)` syscall. `FileManager`'s
    /// `attributesOfItem` fetches the *entire* attribute set (owner,
    /// permissions, size, every timestamp) and bridges it into an NSDictionary
    /// — dozens of times more work than we need. The monitoring scan runs every
    /// few seconds over every session file, so this hot path takes only the one
    /// field it uses.
    static func mtime(_ path: String) -> Date {
        var info = stat()
        guard stat(path, &info) == 0 else { return .distantPast }
        return Date(timeIntervalSince1970:
            TimeInterval(info.st_mtimespec.tv_sec)
            + TimeInterval(info.st_mtimespec.tv_nsec) / 1_000_000_000)
    }

    private static func latestDate(_ lhs: Date?, _ rhs: Date?) -> Date? {
        switch (lhs, rhs) {
        case let (lhs?, rhs?): return max(lhs, rhs)
        case let (lhs?, nil): return lhs
        case let (nil, rhs?): return rhs
        case (nil, nil): return nil
        }
    }

    private static func isLater(_ lhs: Date?, than rhs: Date?) -> Bool {
        guard let lhs else { return false }
        guard let rhs else { return true }
        return lhs.timeIntervalSince(rhs) > 0.5
    }
}

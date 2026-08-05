import Foundation

/// Remote transcript sync roots — the bridge between Agent Island on this Mac
/// and Claude Code / Codex sessions running on servers.
///
/// The server-side `agent-island-sync.sh` mirrors transcripts into
/// `~/Library/Application Support/AgentIsland/Remote/<host>/`:
///   Remote/<host>/claude/          ← rsync of ~/.claude/projects
///   Remote/<host>/codex/           ← rsync of ~/.codex/sessions
///   Remote/<host>/codex-archived/  ← rsync of ~/.codex/archived_sessions
///
/// Every host directory that exists on disk is picked up automatically — no
/// settings entry needed. `host` names are used for display, for the
/// (tool, host, sessionId) dedupe key, and to keep a remote turn from ever
/// being auto-resumed on this machine.
enum RemoteSessionStore {
    /// The synthetic host for this machine's own transcripts.
    static let localHost = "local"

    static func remoteRoot() -> URL {
        FileManager.default.homeDirectoryForCurrentUser
            .appendingPathComponent("Library/Application Support/AgentIsland/Remote", isDirectory: true)
    }

    /// Every configured remote host (GUI-managed via RemoteServerStore), as
    /// long as its sync mirror exists on disk. Newest-added first.
    static func hosts() -> [(host: String, url: URL)] {
        RemoteServerPersistence.load()
            .compactMap { server in
                let url = remoteRoot().appendingPathComponent(server.name, isDirectory: true)
                var isDir: ObjCBool = false
                guard FileManager.default.fileExists(atPath: url.path, isDirectory: &isDir),
                      isDir.boolValue
                else { return nil }
                return (server.name, url)
            }
    }

    /// Claude project roots for every remote host that has synced them.
    static func claudeRoots() -> [URL] {
        hosts().map { $0.url.appendingPathComponent("claude", isDirectory: true) }
            .filter { FileManager.default.fileExists(atPath: $0.path) }
    }

    /// Codex session roots (live + archived) for every remote host.
    static func codexRoots() -> [URL] {
        var out: [URL] = []
        for (_, root) in hosts() {
            let live = root.appendingPathComponent("codex", isDirectory: true)
            if FileManager.default.fileExists(atPath: live.path) { out.append(live) }
            let archived = root.appendingPathComponent("codex-archived", isDirectory: true)
            if FileManager.default.fileExists(atPath: archived.path) { out.append(archived) }
        }
        return out
    }

    /// Claude transcript index across all remote hosts, keyed `"<host>:<sid>"`
    /// so identical UUIDs on different servers can't collide.
    static func remoteClaudeTranscriptIndex() -> [String: String] {
        var out: [String: String] = [:]
        for (host, root) in hosts() {
            let projects = root.appendingPathComponent("claude", isDirectory: true)
            guard let enumerator = FileManager.default.enumerator(atPath: projects.path) else { continue }
            for case let rel as String in enumerator where rel.hasSuffix(".jsonl") {
                // Same subagent exclusions as the local index.
                if rel.contains("/subagents/") { continue }
                if ((rel as NSString).lastPathComponent).hasPrefix("agent-") { continue }
                let path = projects.path + "/" + rel
                let sid = ((rel as NSString).lastPathComponent as NSString).deletingPathExtension
                out["\(host):\(sid)"] = path
            }
        }
        return out
    }
}

import Foundation
import Combine

/// Pulls Claude Code / Codex transcripts from the servers configured in
/// `RemoteServerStore` into `RemoteSessionStore.remoteRoot()`, where the cost
/// scanners and the session monitor pick them up automatically.
///
/// Runs on a fixed interval (3 min) plus on demand. All ssh/rsync work runs
/// off-main via detached tasks so a slow or wedged server never blocks the UI.
@MainActor
final class RemoteSyncEngine: ObservableObject {
    static let shared = RemoteSyncEngine()
    private init() {}

    @Published private(set) var isSyncing = false
    @Published private(set) var lastSyncAt: Date?
    /// "agent-host (codex)" style phase label while a pull is in flight.
    @Published private(set) var currentActivity: String?

    static let syncInterval: TimeInterval = 180
    private var timer: Timer?

    func start() {
        guard timer == nil else { return }
        timer = Timer.scheduledTimer(withTimeInterval: Self.syncInterval, repeats: true) { [weak self] _ in
            guard let self else { return }
            Task { @MainActor in await self.syncAll() }
        }
        Task { @MainActor in await syncAll() }
    }

    func stop() {
        timer?.invalidate()
        timer = nil
    }

    func syncAll() async {
        guard !isSyncing else {
            NSLog("AgentIsland remote sync: already syncing, skip")
            return
        }
        let servers = RemoteServerStore.shared.servers
        guard !servers.isEmpty else {
            NSLog("AgentIsland remote sync: no servers configured")
            return
        }
        isSyncing = true
        defer { isSyncing = false; currentActivity = nil }
        NSLog("AgentIsland remote sync: pulling %d server(s)", servers.count)

        for server in servers {
            currentActivity = server.name
            let result: String? = await Task.detached(priority: .utility) {
                await Self.pull(server)
            }.value
            if let result {
                NSLog("AgentIsland remote sync: %@ failed: %@", server.name, result)
            } else {
                NSLog("AgentIsland remote sync: %@ OK", server.name)
            }
            RemoteServerStore.shared.updateStatus(
                server,
                lastSyncedAt: result == nil ? Date() : nil,
                lastError: result
            )
        }
        lastSyncAt = Date()
    }

    // MARK: - One host's pull (nonisolated, runs off-main)

    nonisolated private static let sshPath = "/usr/bin/ssh"
    nonisolated private static let rsyncPath = "/usr/bin/rsync"
    /// macOS ships openrsync, which does NOT support `--append-verify` —
    /// stick to the portable subset. `--inplace` is what matters for
    /// append-only JSONL transcripts (no temp-copy swap on a live file).
    nonisolated private static let rsyncOptions = ["-az", "--inplace", "-e", "ssh"]

    /// Returns an error string, or nil on success.
    nonisolated private static func pull(_ server: RemoteServerStore.Server) async -> String? {
        // Reachability + directory probe in ONE ssh round-trip (was 4).
        // The remote shell echoes every existing transcript dir, one per line.
        // Trailing `; true`: the loop's exit code is the LAST `[ -d ]` test,
        // which fails when the final candidate dir is missing — that must not
        // be read as an ssh failure.
        let probe = "for d in \"$HOME/.claude/projects\" \"$HOME/.config/claude/projects\" \"$HOME/.codex/sessions\" \"$HOME/.codex/archived_sessions\"; do [ -d \"$d\" ] && echo \"$d\"; done; true"
        let ping = run(sshPath, ["-o", "ConnectTimeout=8", server.sshTarget, probe], timeout: 20)
        guard ping.status == 0 else {
            let detail = ping.output.trimmingCharacters(in: .whitespacesAndNewlines)
            return detail.isEmpty ? "cannot reach \(server.sshTarget)" : detail
        }

        let dirs = ping.output
            .split(separator: "\n")
            .map(String.init)
            .filter { !$0.isEmpty }
        guard !dirs.isEmpty else {
            return "no transcript directories found on \(server.name)"
        }

        let root = RemoteSessionStore.remoteRoot().appendingPathComponent(server.name)
        let fm = FileManager.default
        for sub in ["claude", "codex", "codex-archived"] {
            try? fm.createDirectory(
                at: root.appendingPathComponent(sub, isDirectory: true),
                withIntermediateDirectories: true
            )
        }

        // Pull every existing dir in parallel — one big first sync (hundreds
        // of MB over tailscale) shouldn't serialize claude and codex.
        var failures: [String] = []
        await withTaskGroup(of: (String, String?).self) { group in
            for dir in dirs {
                let dest = destFor(dir, root: root)
                group.addTask {
                    (dir, Self.rsyncPull(target: server.sshTarget, remoteDir: dir, dest: dest))
                }
            }
            for await (dir, error) in group {
                if let error { failures.append("\(dir): \(error)") }
            }
        }
        return failures.isEmpty ? nil : failures.joined(separator: "; ")
    }

    /// Maps a remote transcript dir to its local mirror subdir.
    nonisolated private static func destFor(_ remoteDir: String, root: URL) -> String {
        let lower = remoteDir.lowercased()
        if lower.contains("archived") { return root.appendingPathComponent("codex-archived").path }
        if lower.contains("codex") { return root.appendingPathComponent("codex").path }
        return root.appendingPathComponent("claude").path
    }

    nonisolated private static func rsyncPull(target: String, remoteDir: String, dest: String) -> String? {
        let remote = "\(target):\(remoteDir)/"
        let result = run(rsyncPath, rsyncOptions + [remote, dest], timeout: 300)
        return result.status == 0 ? nil : result.output
    }

    private struct RunResult {
        let status: Int32
        let output: String
    }

    nonisolated private static func run(_ executable: String, _ arguments: [String], timeout: TimeInterval) -> RunResult {
        let process = Process()
        process.executableURL = URL(fileURLWithPath: executable)
        process.arguments = arguments
        let out = Pipe()
        let err = Pipe()
        process.standardOutput = out
        process.standardError = err
        do {
            try process.run()
        } catch {
            return RunResult(status: -1, output: error.localizedDescription)
        }

        // Enforce a hard timeout so a wedged rsync can't hang the engine.
        let deadline = DispatchTime.now() + timeout
        while process.isRunning {
            if DispatchTime.now() > deadline {
                process.terminate()
                return RunResult(status: -1, output: "timed out after \(Int(timeout))s")
            }
            Thread.sleep(forTimeInterval: 0.05)
        }

        let outData = out.fileHandleForReading.readDataToEndOfFile()
        let errData = err.fileHandleForReading.readDataToEndOfFile()
        let text = String(decoding: outData + errData, as: UTF8.self)
        return RunResult(status: process.terminationStatus, output: text)
    }
}

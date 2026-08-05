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
        guard !isSyncing else { return }
        let servers = RemoteServerStore.shared.servers
        guard !servers.isEmpty else { return }
        isSyncing = true
        defer { isSyncing = false }

        for server in servers {
            let result: String? = await Task.detached(priority: .utility) {
                Self.pull(server)
            }.value
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
    nonisolated private static let rsyncOptions = ["-az", "--inplace", "--append-verify", "-e", "ssh"]

    /// Returns an error string, or nil on success.
    nonisolated private static func pull(_ server: RemoteServerStore.Server) -> String? {
        // Reachability gate: if we can't run a trivial command over ssh,
        // every dir probe below would silently skip and the pull would look
        // "successful" with nothing synced.
        let ping = run(sshPath, ["-o", "ConnectTimeout=8", server.sshTarget, "true"], timeout: 15)
        guard ping.status == 0 else {
            let detail = ping.output.trimmingCharacters(in: .whitespacesAndNewlines)
            return detail.isEmpty ? "cannot reach \(server.sshTarget)" : detail
        }

        let root = RemoteSessionStore.remoteRoot().appendingPathComponent(server.name)
        let fm = FileManager.default
        for sub in ["claude", "codex", "codex-archived"] {
            try? fm.createDirectory(
                at: root.appendingPathComponent(sub, isDirectory: true),
                withIntermediateDirectories: true
            )
        }

        // Claude Code transcripts live at ~/.claude/projects on macOS/Linux
        // and ~/.config/claude/projects on XDG setups. Pull the first one
        // that exists.
        for dir in ["~/.claude/projects", "~/.config/claude/projects"] {
            guard dirExists(target: server.sshTarget, dir: dir) else { continue }
            if let error = rsyncPull(
                target: server.sshTarget,
                remoteDir: dir,
                dest: root.appendingPathComponent("claude").path
            ) {
                return error
            }
            break
        }

        if dirExists(target: server.sshTarget, dir: "~/.codex/sessions") {
            if let error = rsyncPull(
                target: server.sshTarget,
                remoteDir: "~/.codex/sessions",
                dest: root.appendingPathComponent("codex").path
            ) {
                return error
            }
        }
        if dirExists(target: server.sshTarget, dir: "~/.codex/archived_sessions") {
            if let error = rsyncPull(
                target: server.sshTarget,
                remoteDir: "~/.codex/archived_sessions",
                dest: root.appendingPathComponent("codex-archived").path
            ) {
                return error
            }
        }
        return nil
    }

    nonisolated private static func dirExists(target: String, dir: String) -> Bool {
        let result = run(sshPath, ["-o", "ConnectTimeout=8", target, "test", "-d", dir], timeout: 15)
        return result.status == 0
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

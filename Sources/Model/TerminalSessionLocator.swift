import AppKit
import Darwin
import Foundation

/// Given a CLI thread the turn alarm is about to reopen, locate the live CLI
/// process that owns it and bring the user back to *that* terminal instead of
/// spawning a fresh Terminal.app window — which, for terminal-first users,
/// reads as a wrong jump into an app they never use.
///
/// Resolution order: focus the exact tab (opt-in, AppleScript) → activate the
/// terminal app → report the process is gone so the caller can resume.
enum TerminalSessionLocator {
    enum ReturnOutcome {
        case focused
        case appActivated
        case processGone
    }

    static func returnToSession(
        provider: AlertEngine.Provider,
        thread: ActivityMonitor.ActiveThread,
        focusTab: Bool
    ) -> ReturnOutcome {
        guard let pid = activeProcess(provider: provider, thread: thread),
              let appURL = terminalAppURL(for: pid)
        else { return .processGone }
        if focusTab, focusTab(appURL: appURL, thread: thread) {
            return .focused
        }
        if activate(appURL: appURL) {
            return .appActivated
        }
        return .processGone
    }

    /// The first app bundle on the CLI process's ancestor chain — the
    /// terminal the user is actually in. Fails open (nil) for daemonized /
    /// tmux / ssh-hosted sessions whose chain terminates at launchd.
    static func terminalAppURL(for pid: pid_t) -> URL? {
        var chain = [pid]
        chain += AgentHostAppResolver.ancestorPids(of: pid).sorted()
        for candidate in chain {
            guard let executable = executablePath(of: candidate),
                  let range = executable.range(of: ".app/", options: .backwards)
            else { continue }
            let bundlePath = String(executable[..<range.lowerBound]) + ".app"
            if FileManager.default.fileExists(atPath: bundlePath) {
                return URL(fileURLWithPath: bundlePath)
            }
        }
        return nil
    }

    /// Best-effort resume in a fresh window of the user's terminal: Ghostty
    /// accepts `open -na <app> --args -e <command…>`; any other terminal
    /// returns false so the caller can surface the command text instead.
    static func resumeInTerminal(
        executable: String,
        arguments: [String],
        cwd: String,
        appURL: URL?
    ) -> Bool {
        guard let appURL, bundleIdentifier(for: appURL) == "com.mitchellh.ghostty" else { return false }
        let binary = CLILocator.path(for: executable == "codex" ? .codex : .claude) ?? executable
        let process = Process()
        process.executableURL = URL(fileURLWithPath: "/usr/bin/open")
        var args = ["-na", appURL.path]
        if !cwd.isEmpty { args += ["--working-directory", cwd] }
        args += ["--args", "-e", binary] + arguments
        process.arguments = args
        process.standardOutput = Pipe()
        process.standardError = Pipe()
        do {
            try process.run()
            process.waitUntilExit()
            return process.terminationStatus == 0
        } catch {
            return false
        }
    }

    // MARK: - Process location

    private static func activeProcess(
        provider: AlertEngine.Provider,
        thread: ActivityMonitor.ActiveThread
    ) -> pid_t? {
        let name = provider == .claude ? "claude" : "codex"
        let candidates = AgentHostAppResolver.cliPids(named: name)
        guard !candidates.isEmpty else { return nil }
        // Exact first: a CLI started with `--resume <id>` / `exec resume <id>`
        // carries the session id on its own command line.
        if !thread.sessionId.isEmpty,
           let pid = candidates.first(where: { commandLine(of: $0)?.contains(thread.sessionId) == true }) {
            return pid
        }
        // Otherwise match on working directory; pick the newest starter when
        // several sessions share the project (the one that just finished).
        let target = AgentHostAppResolver.normalize(thread.cwd)
        let matches = candidates.filter { pid in
            guard let cwd = AgentHostAppResolver.workingDirectory(of: pid) else { return false }
            return AgentHostAppResolver.normalize(cwd) == target
        }
        return matches.max { lhs, rhs in
            (startTime(of: lhs) ?? .distantPast) < (startTime(of: rhs) ?? .distantPast)
        }
    }

    private static func commandLine(of pid: pid_t) -> String? {
        var mib: [Int32] = [CTL_KERN, KERN_PROCARGS2, pid]
        var size = 0
        guard sysctl(&mib, 3, nil, &size, nil, 0) == 0, size > 0 else { return nil }
        var buffer = [CChar](repeating: 0, count: size)
        guard sysctl(&mib, 3, &buffer, &size, nil, 0) == 0 else { return nil }
        return String(bytes: buffer, encoding: .utf8)
    }

    /// `kp_proc.p_starttime` is a boot-relative timeval, so convert to an
    /// absolute date via "now minus elapsed".
    private static func startTime(of pid: pid_t) -> Date? {
        var info = kinfo_proc()
        var size = MemoryLayout<kinfo_proc>.size
        var mib: [Int32] = [CTL_KERN, KERN_PROC, KERN_PROC_PID, pid]
        guard sysctl(&mib, 4, &info, &size, nil, 0) == 0, size > 0 else { return nil }
        let tv = info.kp_proc.p_starttime
        return Date(timeIntervalSinceNow: -Double(tv.tv_sec) - Double(tv.tv_usec) / 1_000_000)
    }

    private static func executablePath(of pid: pid_t) -> String? {
        var buffer = [CChar](repeating: 0, count: 4 * 1024)
        guard proc_pidpath(pid, &buffer, UInt32(buffer.count)) > 0 else { return nil }
        return String(cString: buffer)
    }

    private static func bundleIdentifier(for url: URL) -> String? {
        Bundle(url: url)?.bundleIdentifier
    }

    // MARK: - Activation

    private static func activate(appURL: URL) -> Bool {
        guard let app = NSRunningApplication(url: appURL) else { return false }
        if #available(macOS 14.0, *) {
            return app.activate(options: [.activateAllWindows, .activateIgnoringOtherApps])
        }
        return app.activateWithOptions([.activateAllWindows, .activateIgnoringOtherApps])
    }

    // MARK: - Tab focus (experimental, AppleScript)

    /// Ghostty's scriptable surface exposes window/tab enumeration and tab
    /// ids, but its specifier support is thin and version-dependent — the
    /// whole step is best-effort: any failure returns false and the caller
    /// falls back to activating the app.
    private static func focusTab(appURL: URL, thread: ActivityMonitor.ActiveThread) -> Bool {
        let appName = appURL.deletingPathExtension().lastPathComponent
        guard !appName.isEmpty else { return false }
        let script = """
        tell application "\(appName)"
            set out to ""
            repeat with w in windows
                repeat with t in tabs of w
                    try
                        set out to out & (id of t as text) & "\\t" & (name of t) & linefeed
                    end try
                end repeat
            end repeat
            return out
        end tell
        """
        guard let output = runOSAScript(script) else { return false }
        let project = (thread.cwd as NSString).lastPathComponent
        var targetID: String?
        for line in output.split(separator: "\n") {
            let parts = line.split(separator: "\t", maxSplits: 1).map(String.init)
            guard parts.count == 2 else { continue }
            let name = parts[1]
            let matches = (!project.isEmpty && name.contains(project))
                || (!thread.label.isEmpty && name.contains(thread.label))
            if matches {
                targetID = parts[0]
                break
            }
        }
        guard let targetID else { return false }
        let activateScript = """
        tell application "\(appName)"
            activate
            activate (tab id "\(targetID)")
        end tell
        """
        return runOSAScript(activateScript) != nil
    }

    private static func runOSAScript(_ script: String) -> String? {
        let process = Process()
        process.executableURL = URL(fileURLWithPath: "/usr/bin/osascript")
        process.arguments = ["-e", script]
        let out = Pipe()
        let err = Pipe()
        process.standardOutput = out
        process.standardError = err
        do {
            try process.run()
            process.waitUntilExit()
            guard process.terminationStatus == 0 else { return nil }
            let data = out.fileHandleForReading.readDataToEndOfFile()
            return String(data: data, encoding: .utf8)
        } catch {
            return nil
        }
    }
}

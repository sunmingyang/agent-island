import AppKit
import UserNotifications
import Foundation

@MainActor
enum TurnAlarmNavigator {
    static func open(provider: AlertEngine.Provider, thread: ActivityMonitor.ActiveThread?) {
        switch provider {
        case .codex:
            openCodex(thread: thread)
        case .claude:
            openClaude(thread: thread)
        }
    }

    private static let codexBundleID = "com.openai.codex"

    private static func openCodex(thread: ActivityMonitor.ActiveThread?) {
        if let id = sanitizedCodexThreadID(thread?.sessionId),
           let url = URL(string: "codex://threads/\(id)"),
           let appURL = appURL(forScheme: url, bundleID: codexBundleID) {
            let config = NSWorkspace.OpenConfiguration()
            config.activates = true
            // Deliver the deep link to the *specific* Codex Desktop we resolved,
            // not whatever LaunchServices treats as the codex:// default. The
            // scheme is claimed by a pile of stale duplicates (old Codex
            // Framework helper copies, Sparkle Updater.app launchers, the
            // computer-use helper) — routing through the default handler can
            // wake one of those instead of the window the user is looking at.
            NSWorkspace.shared.open([url], withApplicationAt: appURL, configuration: config) { app, error in
                Task { @MainActor in
                    if let app {
                        app.activate(options: [.activateAllWindows, .activateIgnoringOtherApps])
                    } else if error != nil {
                        codexCLIFallback(thread: thread)
                    } else {
                        activate(bundleIdentifier: codexBundleID)
                    }
                }
            }
            return
        }
        codexCLIFallback(thread: thread)
    }

    private static func codexCLIFallback(thread: ActivityMonitor.ActiveThread?) {
        if let thread, openCLIResume(
            executable: "codex",
            arguments: ["resume", thread.sessionId],
            thread: thread,
            fallbackBundleID: codexBundleID
        ) {
            return
        }
        activate(bundleIdentifier: codexBundleID)
    }

    /// The app bundle a scheme URL should be delivered to. Prefer the running
    /// instance of the expected bundle id (so the URL lands in the window that's
    /// actually open) before falling back to LaunchServices' default handler
    /// and finally a by-id lookup.
    private static func appURL(forScheme url: URL, bundleID: String) -> URL? {
        if let running = NSWorkspace.shared.runningApplications
            .first(where: { $0.bundleIdentifier == bundleID })?.bundleURL {
            return running
        }
        if let byScheme = NSWorkspace.shared.urlForApplication(toOpen: url) {
            return byScheme
        }
        return NSWorkspace.shared.urlForApplication(withBundleIdentifier: bundleID)
    }

    private static func sanitizedCodexThreadID(_ raw: String?) -> String? {
        guard let raw, !raw.isEmpty else { return nil }
        let allowed = CharacterSet(charactersIn: "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_")
        guard raw.unicodeScalars.allSatisfy({ allowed.contains($0) }) else { return nil }
        return raw
    }

    private static let claudeBundleID = "com.anthropic.claudefordesktop"

    private static func openClaude(thread: ActivityMonitor.ActiveThread?) {
        // Split by where the session actually lives:
        //
        // - Desktop sessions: bring Claude Desktop to the front, nothing more.
        //   There is NO deep link that lands on an existing conversation —
        //   `claude://resume?sessionId=…` is not a real endpoint (Desktop only
        //   registers `claude://…/new`, the CLI registers `claude-cli://open`,
        //   both of which start a NEW session). 1.5.1 briefly resumed these in
        //   a Terminal via `claude --resume`, which does continue the thread —
        //   but a Desktop user clicking "Open thread" expects their Desktop
        //   window, and a Terminal popping up reads as a wrong jump.
        //
        // - CLI sessions: resume for real, the way the auto-trigger engine
        //   does — `claude --resume <id>` in a terminal from the session's
        //   own cwd. That user lives in a terminal already.
        if thread?.launchTarget == .claudeDesktop {
            activate(bundleIdentifier: claudeBundleID)
            // Best possible compensation for the missing deep link: put the
            // session's title on the clipboard and tell the user, so locating
            // the conversation is one paste in Claude's search instead of a
            // scroll hunt through the sidebar.
            if let label = thread?.label, !label.isEmpty, label != L10n.tr("Demo thread") {
                let pasteboard = NSPasteboard.general
                pasteboard.clearContents()
                pasteboard.setString(label, forType: .string)
                postClipboardHint(label: label)
            }
            return
        }
        if let thread, openCLIResume(
            executable: "claude",
            arguments: ["--resume", thread.sessionId],
            thread: thread,
            fallbackBundleID: claudeBundleID
        ) {
            return
        }
        activate(bundleIdentifier: claudeBundleID)
    }

    /// Claude Desktop has no URL that lands on an existing conversation (its
    /// scheme only registers new-session entry points), so after fronting the
    /// app we surface a quiet notification explaining the clipboard assist.
    private static func postClipboardHint(label: String) {
        let content = UNMutableNotificationContent()
        content.title = L10n.tr("Session name copied")
        content.body = L10n.tr("Paste it in Claude's search to jump to “%@”.", label)
        let request = UNNotificationRequest(
            identifier: "agent-island-claude-clipboard-hint",
            content: content,
            trigger: nil
        )
        UNUserNotificationCenter.current().add(request, withCompletionHandler: nil)
    }

    private static func activate(bundleIdentifier: String) {
        if let app = NSWorkspace.shared.runningApplications.first(where: { $0.bundleIdentifier == bundleIdentifier }) {
            app.activate(options: [.activateAllWindows, .activateIgnoringOtherApps])
            return
        }
        guard let url = NSWorkspace.shared.urlForApplication(withBundleIdentifier: bundleIdentifier) else { return }
        NSWorkspace.shared.openApplication(at: url, configuration: NSWorkspace.OpenConfiguration())
    }

    private static func openCLIResume(
        executable: String,
        arguments: [String],
        thread: ActivityMonitor.ActiveThread,
        fallbackBundleID: String
    ) -> Bool {
        guard !thread.sessionId.isEmpty else { return false }
        let command = resumeCommand(executable: executable, arguments: arguments, cwd: thread.cwd)
        let sessionId = thread.sessionId
        // osascript blocks until Terminal handles the Apple Event — on the
        // first run that includes the TCC consent prompt, which can sit for
        // minutes. waitUntilExit on the main actor froze the whole app, so
        // the run happens off-main and the fallbacks hop back for AppKit.
        Task.detached(priority: .userInitiated) {
            if runTerminalCommand(command) { return }
            await MainActor.run {
                if openCommandFile(command: command, executable: executable, sessionId: sessionId) { return }
                activate(bundleIdentifier: fallbackBundleID)
            }
        }
        return true
    }

    nonisolated private static func runTerminalCommand(_ command: String) -> Bool {
        let script = """
        tell application "Terminal"
            activate
            do script "\(appleScriptString(command))"
        end tell
        """
        let process = Process()
        process.executableURL = URL(fileURLWithPath: "/usr/bin/osascript")
        process.arguments = ["-e", script]
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

    private static func openCommandFile(command: String, executable: String, sessionId: String) -> Bool {
        guard let root = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first else {
            return false
        }
        let dir = root
            .appendingPathComponent("AgentIsland", isDirectory: true)
            .appendingPathComponent("ResumeCommands", isDirectory: true)
        do {
            try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
            let url = dir.appendingPathComponent("resume-\(safeFileComponent(executable))-\(safeFileComponent(sessionId)).command")
            let body = """
            #!/bin/zsh
            \(command)
            """
            try body.write(to: url, atomically: true, encoding: .utf8)
            try FileManager.default.setAttributes([.posixPermissions: 0o700], ofItemAtPath: url.path)
            return NSWorkspace.shared.open(url)
        } catch {
            return false
        }
    }

    private static func resumeCommand(executable: String, arguments: [String], cwd: String) -> String {
        var parts = [
            "export PATH=\"$HOME/.local/bin:/opt/homebrew/bin:/usr/local/bin:/usr/bin:/bin:/usr/sbin:/sbin:$PATH\""
        ]
        if isUsableDirectory(cwd) {
            parts.append("cd \(shellQuote(cwd)) || exit 1")
        }
        // nvm/bun/npm-global installs live outside the exported PATH;
        // CLILocator already probes those homes for the trigger engine.
        let binary = CLILocator.path(for: executable == "codex" ? .codex : .claude) ?? executable
        parts.append("exec \(shellJoin([binary] + arguments))")
        return parts.joined(separator: "; ")
    }

    private static func isUsableDirectory(_ path: String) -> Bool {
        guard !path.isEmpty else { return false }
        var isDirectory: ObjCBool = false
        return FileManager.default.fileExists(atPath: path, isDirectory: &isDirectory) && isDirectory.boolValue
    }

    private static func shellJoin(_ parts: [String]) -> String {
        parts.map(shellQuote).joined(separator: " ")
    }

    private static func shellQuote(_ raw: String) -> String {
        guard !raw.isEmpty else { return "''" }
        return "'" + raw.replacingOccurrences(of: "'", with: "'\\''") + "'"
    }

    nonisolated private static func appleScriptString(_ raw: String) -> String {
        raw
            .replacingOccurrences(of: "\\", with: "\\\\")
            .replacingOccurrences(of: "\"", with: "\\\"")
            .replacingOccurrences(of: "\n", with: "\\n")
    }

    private static func safeFileComponent(_ raw: String) -> String {
        let allowed = CharacterSet.alphanumerics.union(CharacterSet(charactersIn: "-_"))
        let scalars = raw.unicodeScalars.map { allowed.contains($0) ? Character($0) : "-" }
        let text = String(scalars)
        return text.isEmpty ? "session" : String(text.prefix(80))
    }
}

import Foundation

enum TriggerTool: String, Codable, CaseIterable {
    case claude
    case codex
    case gemini
    case grok
    case cursor

    var display: String {
        switch self {
        case .claude: return "Claude"
        case .codex: return "Codex"
        case .gemini: return "Gemini"
        case .grok: return "Grok"
        case .cursor: return "Cursor"
        }
    }

    /// Only Claude and Codex expose a `--resume <id>` CLI contract, so only
    /// they can carry auto-triggers. The other three still flow through the
    /// monitoring scan (island logo + turn alarms) — session STATUS is
    /// five-provider, session RESUME is two.
    var supportsAutoResume: Bool {
        switch self {
        case .claude, .codex: return true
        case .gemini, .grok, .cursor: return false
        }
    }
}

/// When a trigger fires. `afterReset` rides the provider's real 5-hour-window
/// reset (detected by `UsageStore`), so it fires at the actual reset instant
/// rather than on a fixed clock. `everyHours` is a plain fixed interval.
enum TriggerMode: String, Codable, CaseIterable {
    case afterReset
    case everyHours
}

/// One auto-trigger: resume `sessionId` in `tool` with `message` when `mode`
/// is satisfied. Persisted as JSON in UserDefaults by `TriggerStore`.
struct Trigger: Codable, Identifiable, Equatable {
    static var defaultMessage: String { L10n.tr("Continue") }

    var id: String
    var tool: TriggerTool
    var sessionId: String
    var label: String
    var cwd: String
    var message: String
    var mode: TriggerMode
    var everyHours: Int
    var enabled: Bool
    var lastFired: Date?

    init(
        id: String = UUID().uuidString,
        tool: TriggerTool,
        sessionId: String,
        label: String,
        cwd: String,
        message: String = Self.defaultMessage,
        mode: TriggerMode = .afterReset,
        everyHours: Int = 5,
        enabled: Bool = true,
        lastFired: Date? = nil
    ) {
        self.id = id
        self.tool = tool
        self.sessionId = sessionId
        self.label = label
        self.cwd = cwd
        self.message = message
        self.mode = mode
        self.everyHours = everyHours
        self.enabled = enabled
        self.lastFired = lastFired
    }
}

/// Resolves CLI binaries by probing known install locations. LaunchServices
/// hands GUI apps a stripped PATH (`/usr/bin:/bin:/usr/sbin:/sbin`), so a
/// `which` call would miss every Homebrew/nvm/Bun install — same reasoning as
/// `ClaudeCredentials.locateClaudeBinary`.
enum CLILocator {
    static func path(for tool: TriggerTool) -> String? {
        switch tool {
        case .claude: return locate("claude")
        case .codex: return locate("codex")
        case .gemini: return locate("gemini")
        case .grok: return locate("grok")
        case .cursor: return nil
        }
    }

    private static func locate(_ name: String) -> String? {
        let home = NSHomeDirectory()
        let candidates = [
            "/opt/homebrew/bin/\(name)",
            "/usr/local/bin/\(name)",
            "\(home)/.local/bin/\(name)",
            "\(home)/.bun/bin/\(name)",
            "\(home)/.npm-global/bin/\(name)",
            // hermes-managed npm prefix (owner's machine): global CLIs land
            // here, invisible to every conventional prefix above.
            "\(home)/.hermes/node/bin/\(name)",
        ]
        for path in candidates where FileManager.default.isExecutableFile(atPath: path) {
            return path
        }
        let nvmRoot = "\(home)/.nvm/versions/node"
        if let versions = try? FileManager.default.contentsOfDirectory(atPath: nvmRoot) {
            for version in versions.sorted(by: >) {
                let candidate = "\(nvmRoot)/\(version)/bin/\(name)"
                if FileManager.default.isExecutableFile(atPath: candidate) {
                    return candidate
                }
            }
        }
        return nil
    }
}

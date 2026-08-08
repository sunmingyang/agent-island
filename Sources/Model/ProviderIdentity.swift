import SwiftUI

/// One authority for "what does this provider look like" at the alarm/status
/// layer. Grew out of 2.1.1's five-wide session monitoring: a dozen
/// `provider == .claude ? A : B` ternaries each silently rendered Grok and
/// Gemini as Codex (wrong name in the alarm, wrong logo, wrong accent).
/// Every consumer now reads the same switch, so a sixth provider is one
/// case away and the compiler walks the call sites.
extension DisplayProvider {
    /// Bridge into the alarm/status domain. Both enums carry the same five
    /// members; the raw-value hop keeps them decoupled without a 5-arm
    /// switch to maintain twice.
    var alertProvider: AlertEngine.Provider {
        AlertEngine.Provider(rawValue: rawValue) ?? .claude
    }
}

extension TriggerTool {
    /// Trigger rows carry the same five identities; hop via raw value like
    /// the DisplayProvider bridge above.
    var accentColor: Color {
        (AlertEngine.Provider(rawValue: rawValue) ?? .claude).accent
    }
}

extension AlertEngine.Provider {
    var displayName: String {
        switch self {
        case .claude: return "Claude Code"
        case .codex: return "Codex"
        case .antigravity: return "Gemini"
        case .grok: return "Grok"
        case .cursor: return "Cursor"
        }
    }

    var accent: Color {
        switch self {
        case .claude: return IslandColor.claude
        case .codex: return IslandColor.codex
        case .antigravity: return IslandColor.antigravity
        case .grok: return IslandColor.grok
        case .cursor: return IslandColor.cursor
        }
    }

    /// CLI executable name, nil where the product is app-only.
    var cliName: String? {
        switch self {
        case .claude: return "claude"
        case .codex: return "codex"
        case .antigravity: return "agy"
        case .grok: return "grok"
        case .cursor: return nil
        }
    }
}

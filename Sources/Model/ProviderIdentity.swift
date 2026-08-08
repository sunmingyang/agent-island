import SwiftUI

/// One authority for "what does this provider look like" at the alarm/status
/// layer. Grew out of 2.1.1's five-wide session monitoring: a dozen
/// `provider == .claude ? A : B` ternaries each silently rendered Grok and
/// Gemini as Codex (wrong name in the alarm, wrong logo, wrong accent).
/// Every consumer now reads the same switch, so a sixth provider is one
/// case away and the compiler walks the call sites.
extension AlertEngine.Provider {
    var displayName: String {
        switch self {
        case .claude: return "Claude Code"
        case .codex: return "Codex"
        case .gemini: return "Gemini"
        case .grok: return "Grok"
        case .cursor: return "Cursor"
        }
    }

    var accent: Color {
        switch self {
        case .claude: return IslandColor.claude
        case .codex: return IslandColor.codex
        case .gemini: return IslandColor.gemini
        case .grok: return IslandColor.grok
        case .cursor: return IslandColor.cursor
        }
    }

    /// CLI executable name, nil where the product is app-only.
    var cliName: String? {
        switch self {
        case .claude: return "claude"
        case .codex: return "codex"
        case .gemini: return "gemini"
        case .grok: return "grok"
        case .cursor: return nil
        }
    }
}

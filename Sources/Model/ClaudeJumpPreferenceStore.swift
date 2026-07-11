import Foundation

/// How "Open thread" treats a Claude *Desktop* session: land in the Desktop
/// app (default), or resume the exact conversation in a terminal via
/// `claude --resume`. Desktop can't jump to a specific chat yet — the
/// `claude://code/<bridge-id>` route exists in its code but sits behind
/// Anthropic's server-side flag — so people who care more about "exact
/// conversation" than "my usual window" can pick the CLI here.
@MainActor
final class ClaudeJumpPreferenceStore: ObservableObject {
    static let shared = ClaudeJumpPreferenceStore()

    private static let key = "MacIsland.claudeJumpPrefersCLI"

    @Published var prefersCLI: Bool {
        didSet { UserDefaults.standard.set(prefersCLI, forKey: Self.key) }
    }

    private init() {
        prefersCLI = UserDefaults.standard.bool(forKey: Self.key)
    }
}

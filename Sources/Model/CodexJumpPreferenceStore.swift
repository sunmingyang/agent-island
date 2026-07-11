import Foundation

/// How "Open thread" treats a Codex session: deep-link into the desktop
/// app's exact thread (default — `codex://threads/<id>` is the app's own
/// official route), or reopen the conversation in a terminal via
/// `codex resume`, for people who live in the CLI.
@MainActor
final class CodexJumpPreferenceStore: ObservableObject {
    static let shared = CodexJumpPreferenceStore()

    private static let key = "MacIsland.codexJumpPrefersCLI"

    @Published var prefersCLI: Bool {
        didSet { UserDefaults.standard.set(prefersCLI, forKey: Self.key) }
    }

    private init() {
        prefersCLI = UserDefaults.standard.bool(forKey: Self.key)
    }
}

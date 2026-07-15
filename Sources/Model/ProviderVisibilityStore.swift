import Foundation

/// Per-provider visibility for the menu-bar island and expanded panel.
/// Two layers:
///   - the manual Settings toggles (`claudeVisible` / `codexVisible`);
///   - machine detection (`claudeDetected` / `codexDetected`): a provider
///     with no CLI footprint on this machine auto-yields its half of the
///     island, so single-subscription users get the solo split layout
///     without hunting for a Settings toggle (top user ask after 1.6.1 —
///     "只订阅一个的时候整个界面看起来非常尴尬").
/// Manual wins once touched: flipping a toggle records intent, and
/// detection stops second-guessing that provider.
@MainActor
final class ProviderVisibilityStore: ObservableObject {
    static let shared = ProviderVisibilityStore()

    private static let claudeKey = "MacIsland.claudeVisible"
    private static let codexKey = "MacIsland.codexVisible"
    private static let claudeTouchedKey = "MacIsland.claudeVisibleTouched"
    private static let codexTouchedKey = "MacIsland.codexVisibleTouched"

    @Published var claudeVisible: Bool {
        didSet {
            UserDefaults.standard.set(claudeVisible, forKey: Self.claudeKey)
            UserDefaults.standard.set(true, forKey: Self.claudeTouchedKey)
        }
    }
    @Published var codexVisible: Bool {
        didSet {
            UserDefaults.standard.set(codexVisible, forKey: Self.codexKey)
            UserDefaults.standard.set(true, forKey: Self.codexTouchedKey)
        }
    }

    /// CLI footprint, evaluated once at launch (three stat calls).
    @Published private(set) var claudeDetected: Bool
    @Published private(set) var codexDetected: Bool

    private init() {
        self.claudeVisible = Pref.seededBool(key: Self.claudeKey, default: true)
        self.codexVisible = Pref.seededBool(key: Self.codexKey, default: true)

        let home = FileManager.default.homeDirectoryForCurrentUser
        func hasDir(_ relative: String) -> Bool {
            var isDir: ObjCBool = false
            let path = home.appendingPathComponent(relative).path
            return FileManager.default.fileExists(atPath: path, isDirectory: &isDir)
                && isDir.boolValue
        }
        self.claudeDetected = hasDir(".claude") || hasDir(".config/claude")
        self.codexDetected = hasDir(".codex")
    }

    /// What the island actually renders. Detection only speaks for a
    /// provider whose toggle was never touched, and only hides one side
    /// when the OTHER side is present — a machine with neither footprint
    /// still shows both instead of a blank island.
    var claudeShown: Bool {
        guard claudeVisible else { return false }
        if UserDefaults.standard.bool(forKey: Self.claudeTouchedKey) { return true }
        return claudeDetected || !codexDetected
    }

    var codexShown: Bool {
        guard codexVisible else { return false }
        if UserDefaults.standard.bool(forKey: Self.codexTouchedKey) { return true }
        return codexDetected || !claudeDetected
    }

    /// Single accessor for call sites that have an `AlertEngine.Provider`
    /// in hand.
    func effectiveVisible(provider: AlertEngine.Provider) -> Bool {
        switch provider {
        case .claude: return claudeShown
        case .codex:  return codexShown
        }
    }
}

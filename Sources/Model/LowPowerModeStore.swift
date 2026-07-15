import Foundation
import Combine

/// User preference for the ambient halo + loading sweep, surfaced in
/// Settings as the two visual-effect modes:
///
///   Calm  (`enabled == true`, the DEFAULT): both surfaces gate on a "glow
///   event" — they appear only while a fetch is in flight, the cursor
///   hovers the island, or an alert is active. At rest the island is a
///   quiet black pill.
///
///   Vivid (`enabled == false`): the halo glow and the cobalt orbit run
///   continuously — full ambience, more per-frame gradient + blur work.
///
/// Calm-by-default is deliberate (2026-07-15): the always-on sweep was the
/// top "light pollution" finding of the first external design review, and
/// restraint is the stronger default. Vivid stays one click away.
///
/// `effectiveEnabled` ORs the user choice with macOS's system-wide Low
/// Power Mode: system battery saving forces Calm regardless of the picker —
/// same convention Apple's own apps follow.
@MainActor
final class LowPowerModeStore: ObservableObject {
    static let shared = LowPowerModeStore()

    private static let key = "AgentIsland.lowPowerMode"

    /// true = Calm (event-gated effects), false = Vivid (continuous).
    @Published var enabled: Bool {
        didSet { UserDefaults.standard.set(enabled, forKey: Self.key) }
    }

    /// Mirrors `ProcessInfo.processInfo.isLowPowerModeEnabled`. Updated
    /// via NSProcessInfoPowerStateDidChange.
    @Published private(set) var systemLowPowerEnabled: Bool

    /// True if either the user opted in OR macOS reports system LPM is on.
    /// Use this in render-gating predicates instead of `enabled` directly.
    var effectiveEnabled: Bool { enabled || systemLowPowerEnabled }

    private var observer: NSObjectProtocol?

    private init() {
        // Calm unless the user explicitly chose Vivid. A missing key means
        // "never touched", which must land on Calm — so read presence, not
        // `bool(forKey:)` (whose false-for-missing would mean Vivid).
        if UserDefaults.standard.object(forKey: Self.key) == nil {
            self.enabled = true
        } else {
            self.enabled = UserDefaults.standard.bool(forKey: Self.key)
        }
        self.systemLowPowerEnabled = ProcessInfo.processInfo.isLowPowerModeEnabled

        observer = NotificationCenter.default.addObserver(
            forName: Notification.Name.NSProcessInfoPowerStateDidChange,
            object: nil,
            queue: .main
        ) { [weak self] _ in
            guard let self else { return }
            Task { @MainActor in
                let now = ProcessInfo.processInfo.isLowPowerModeEnabled
                if now != self.systemLowPowerEnabled {
                    self.systemLowPowerEnabled = now
                }
            }
        }
    }

    deinit {
        if let observer { NotificationCenter.default.removeObserver(observer) }
    }
}

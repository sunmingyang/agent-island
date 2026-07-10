import Foundation

/// UserDefaults key for the subagent-alarm preference. Declared at module scope
/// (not on the `@MainActor` store) so the session scanner — which runs off the
/// main actor — can read the raw flag directly. `UserDefaults` access is
/// thread-safe, so no isolation hop is needed on the scan hot path.
let subagentAlarmDefaultsKey = "AgentIsland.showSubagentAlarms"

/// Whether subagent / child threads (orchestrator-spawned executors) also raise
/// turn alarms — and show up / drive the logo — when they finish.
///
/// Default OFF: a spawned subagent finishes constantly with no human waiting on
/// it, so surfacing them floods the desktop with pop-ups. The session scanner
/// filters them out unless this is on. Turn ON to treat subagent threads like
/// any other session.
@MainActor
final class SubagentAlarmStore: ObservableObject {
    static let shared = SubagentAlarmStore()

    @Published var showSubagentThreads: Bool {
        didSet { UserDefaults.standard.set(showSubagentThreads, forKey: subagentAlarmDefaultsKey) }
    }

    private init() {
        // Missing key → false → subagent threads stay filtered (the safe default).
        self.showSubagentThreads = UserDefaults.standard.bool(forKey: subagentAlarmDefaultsKey)
    }
}

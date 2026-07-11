import Foundation

@MainActor
final class TriggerSafetyStore: ObservableObject {
    static let shared = TriggerSafetyStore()

    private static let enabledKey = "AgentIsland.triggerExecutionEnabled"
    private static let allowedRootsKey = "AgentIsland.triggerAllowedRoots"

    @Published var executionEnabled: Bool {
        didSet { UserDefaults.standard.set(executionEnabled, forKey: Self.enabledKey) }
    }

    @Published private(set) var allowedRoots: Set<String> {
        didSet { UserDefaults.standard.set(Array(allowedRoots).sorted(), forKey: Self.allowedRootsKey) }
    }

    private init() {
        executionEnabled = UserDefaults.standard.object(forKey: Self.enabledKey) == nil
            ? true
            : UserDefaults.standard.bool(forKey: Self.enabledKey)
        UserDefaults.standard.removeObject(forKey: "AgentIsland.triggerDryRun")
        allowedRoots = Set(UserDefaults.standard.stringArray(forKey: Self.allowedRootsKey) ?? [])
    }

    /// The allowlist entry that authorizes a trigger. A real project path
    /// trusts the whole project (all its triggers); a trigger with no cwd —
    /// common for Claude Desktop / home-dir sessions — is trusted individually
    /// by its own id. Never empty, so the approval-skipping resume still
    /// requires an explicit allow. The auto-monitoring scanner never builds a
    /// Trigger, so it can never reach this and can't auto-authorize itself.
    private func authKey(for trigger: Trigger) -> String {
        let root = normalized(trigger.cwd)
        return root.isEmpty ? "trigger:\(trigger.id)" : root
    }

    func isAllowed(_ trigger: Trigger) -> Bool {
        allowedRoots.contains(authKey(for: trigger))
    }

    func setAllowed(_ trigger: Trigger, _ allowed: Bool) {
        let key = authKey(for: trigger)
        if allowed {
            allowedRoots.insert(key)
        } else {
            allowedRoots.remove(key)
        }
    }

    func normalized(_ cwd: String) -> String {
        guard !cwd.isEmpty else { return "" }
        return (cwd as NSString).standardizingPath
    }
}

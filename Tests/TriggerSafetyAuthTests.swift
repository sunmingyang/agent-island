import Foundation

// Isolated stub — Trigger.defaultMessage calls L10n.tr.
enum L10n {
    static func tr(_ key: String) -> String { key }
    static func tr(_ key: String, _ args: CVarArg...) -> String { key }
}

@MainActor
func runTriggerSafetyAuthTests() {
    // Hermetic: clear any allowlist this binary persisted on a prior run
    // BEFORE the singleton reads it in init.
    UserDefaults.standard.removeObject(forKey: "AgentIsland.triggerAllowedRoots")
    let store = TriggerSafetyStore.shared

    func make(cwd: String) -> Trigger {
        Trigger(
            tool: .claude,
            sessionId: "sess-\(UUID().uuidString.prefix(8))",
            label: "t",
            cwd: cwd,
            message: "Continue",
            mode: .afterReset,
            everyHours: 0,
            enabled: true,
            lastFired: nil
        )
    }

    func expect(_ cond: Bool, _ msg: String) {
        if !cond { fatalError("FAIL: \(msg)") }
    }

    // 1. A non-empty-cwd trigger: untrusted until allowed, then allowed.
    let a = make(cwd: "/tmp/projectA")
    expect(!store.isAllowed(a), "fresh trigger must start untrusted")
    store.setAllowed(a, true)
    expect(store.isAllowed(a), "non-empty cwd trigger trusts and reads back allowed")

    // 2. THE REGRESSION CASE: an empty-cwd trigger. This used to be permanently
    //    un-authorizable (setAllowed no-op'd on empty root), so auto-resume for
    //    a Claude Desktop / home-dir session never ran. Now it trusts by id.
    let b = make(cwd: "")
    expect(!store.isAllowed(b), "empty-cwd trigger starts untrusted")
    store.setAllowed(b, true)
    expect(store.isAllowed(b), "empty-cwd trigger can now be trusted (was impossible before the fix)")

    // 3. Trust is per-trigger for empty cwd — one empty-cwd allow must not leak
    //    to a different empty-cwd trigger.
    let c = make(cwd: "")
    expect(!store.isAllowed(c), "a different empty-cwd trigger is NOT trusted by b's allow")

    // 4. Revoke works both ways.
    store.setAllowed(b, false)
    expect(!store.isAllowed(b), "revoke removes trust")

    // 5. Non-empty cwd trusts the whole project (sibling triggers share it) —
    //    the existing project-trust property is preserved.
    let a2 = make(cwd: "/tmp/projectA")
    expect(store.isAllowed(a2), "a sibling trigger in an already-trusted project is allowed")

    print("TriggerSafetyAuthTests GREEN")
}

@main
enum TriggerSafetyAuthTestMain {
    static func main() {
        MainActor.assumeIsolated { runTriggerSafetyAuthTests() }
    }
}

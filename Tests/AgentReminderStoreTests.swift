import AppKit
import Foundation

private enum TestFailure: Error, CustomStringConvertible {
    case assertion(String)

    var description: String {
        switch self {
        case .assertion(let message): return message
        }
    }
}

@discardableResult
private func expect(_ condition: @autoclosure () -> Bool, _ message: String) throws -> Bool {
    guard condition() else { throw TestFailure.assertion(message) }
    return true
}

// The test binary is unbundled, so UserDefaults.standard resolves to a
// process-name domain — isolated from the real app's preferences. The
// runner script deletes the domain afterwards.
private let frontmostKey = "AgentIsland.agentReminderFrontmostSoundOnly"

@MainActor
private func testFrontmostSoundOnlyDefaultsOffAndPersists() throws {
    UserDefaults.standard.removeObject(forKey: frontmostKey)
    let store = AgentReminderStore.shared
    try expect(store.frontmostSoundOnly == false, "#9 chime must default off (silent as before)")

    store.frontmostSoundOnly = true
    try expect(UserDefaults.standard.bool(forKey: frontmostKey), "toggling on must persist")

    store.frontmostSoundOnly = false
    try expect(UserDefaults.standard.object(forKey: frontmostKey) != nil,
               "explicit off must write, not just clear")
    try expect(!UserDefaults.standard.bool(forKey: frontmostKey), "toggling off must persist")

    store.frontmostSoundOnly = true
    try expect(UserDefaults.standard.bool(forKey: frontmostKey), "round trip must land on on")
    UserDefaults.standard.removeObject(forKey: frontmostKey)
}

@MainActor
private func testChimeSoundResolves() throws {
    try expect(AgentReminderStore.shared.makeAlarmSound() != nil,
               "the chime must resolve a sound (Glass fallback) or #9 no-ops silently")
}

@main
private enum AgentReminderStoreTestRunner {
    @MainActor
    static func main() {
        let tests: [(String, @MainActor () throws -> Void)] = [
            ("frontmost chime defaults off and persists", testFrontmostSoundOnlyDefaultsOffAndPersists),
            ("chime sound resolves", testChimeSoundResolves)
        ]

        do {
            for (name, test) in tests {
                try test()
                print("PASS \(name)")
            }
            print("AgentReminderStoreTests GREEN")
        } catch {
            fputs("AgentReminderStoreTests RED: \(error)\n", stderr)
            exit(1)
        }
    }
}

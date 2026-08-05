import Foundation
import Combine

/// GUI-managed list of servers whose Claude Code / Codex transcripts are
/// synced into `RemoteSessionStore.remoteRoot()`.
///
/// Persisted as JSON in UserDefaults so the background cost scanners can read
/// the same list without a MainActor hop (the cost scan runs off-main).
@MainActor
final class RemoteServerStore: ObservableObject {
    static let shared = RemoteServerStore()

    struct Server: Codable, Identifiable, Equatable {
        var id: UUID
        var name: String
        var sshTarget: String
        var lastSyncedAt: Date?
        var lastError: String?

        init(
            id: UUID = UUID(),
            name: String,
            sshTarget: String,
            lastSyncedAt: Date? = nil,
            lastError: String? = nil
        ) {
            self.id = id
            self.name = name
            self.sshTarget = sshTarget
            self.lastSyncedAt = lastSyncedAt
            self.lastError = lastError
        }

        var isHealthy: Bool { lastError == nil }
    }

    @Published private(set) var servers: [Server] {
        didSet { RemoteServerPersistence.persist(servers) }
    }

    private init() {
        servers = RemoteServerPersistence.load()
    }

    @discardableResult
    func add(name: String, sshTarget: String) -> Server? {
        let cleanName = name.trimmingCharacters(in: .whitespacesAndNewlines)
        let cleanTarget = sshTarget.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !cleanName.isEmpty, !cleanTarget.isEmpty else { return nil }
        // A duplicate name would overwrite the same Remote/<name> directory.
        servers.removeAll { $0.name == cleanName }
        let server = Server(name: cleanName, sshTarget: cleanTarget)
        servers.insert(server, at: 0)
        return server
    }

    func remove(_ server: Server) {
        servers.removeAll { $0.id == server.id }
        // Drop the synced mirror so a removed server can't leave ghosts.
        let dir = RemoteSessionStore.remoteRoot().appendingPathComponent(server.name)
        try? FileManager.default.removeItem(at: dir)
    }

    func updateStatus(_ server: Server, lastSyncedAt: Date? = nil, lastError: String? = nil) {
        guard let idx = servers.firstIndex(where: { $0.id == server.id }) else { return }
        servers[idx].lastSyncedAt = lastSyncedAt
        servers[idx].lastError = lastError
    }
}

/// Nonisolated persistence so background scanners can load the server list
/// without touching MainActor state.
enum RemoteServerPersistence {
    private static let storageKey = "AgentIsland.remoteServers.v1"

    static func persist(_ servers: [RemoteServerStore.Server]) {
        guard let data = try? JSONEncoder().encode(servers) else { return }
        UserDefaults.standard.set(data, forKey: storageKey)
    }

    static func load() -> [RemoteServerStore.Server] {
        guard let data = UserDefaults.standard.data(forKey: storageKey),
              let servers = try? JSONDecoder().decode([RemoteServerStore.Server].self, from: data)
        else { return [] }
        return servers
    }
}

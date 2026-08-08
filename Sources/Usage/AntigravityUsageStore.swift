import Foundation

/// Gemini's slice of the panel: the last fetched quota snapshot plus the
/// account identity from oauth_creds.json. Timer-free like GrokUsageStore —
/// it rides `UsageStore.refresh()`'s cadence via `kickRefresh()` behind the
/// same 120s attempt floor, with a UserDefaults snapshot cache so relaunch
/// doesn't blank the strip.
@MainActor
final class AntigravityUsageStore: ObservableObject {
    static let shared = AntigravityUsageStore()

    @Published private(set) var snapshot: AntigravityQuotaSnapshot?
    /// Non-nil while the latest fetch ended in anything but data. Values in
    /// `snapshot` are the preserved last-good numbers in that case.
    @Published private(set) var statusCaption: String?
    @Published private(set) var lastUpdated: Date?
    @Published private(set) var accountEmail: String?
    @Published private(set) var loading = false

    /// Detection is launch-static, same as the other providers.
    let detection: AntigravityAuthDetection

    private var lastAttempt: Date?
    private static let cacheKey = "AntigravityUsageStore.lastSnapshot.v1"
    private static let cacheMaxAge: TimeInterval = 24 * 60 * 60
    private static let minAttemptGap: TimeInterval = 120

    private struct CachedSnapshot: Codable {
        var snapshot: AntigravityQuotaSnapshot
        var updatedAt: Date
    }

    private init() {
        if AppEnvironment.isDemo {
            if AppEnvironment.demoGuestFixturesEnabled {
                let now = Date()
                detection = .oauthPersonal
                snapshot = AntigravityQuotaSnapshot(
                    buckets: [
                        AntigravityModelBucket(
                            modelId: "gemini-3-pro-preview",
                            usedPercent: 0.43,
                            resetAt: now.addingTimeInterval(7 * 3600 + 24 * 60)
                        ),
                        AntigravityModelBucket(
                            modelId: "gemini-3-flash-preview",
                            usedPercent: 0.18,
                            resetAt: now.addingTimeInterval(7 * 3600 + 24 * 60)
                        ),
                    ],
                    tierID: "standard-tier",
                    tierLabel: "Paid"
                )
                lastUpdated = now
            } else {
                detection = .notInstalled
            }
            return
        }
        detection = AntigravityCredentials.detect()
        guard detection == .oauthPersonal else { return }
        loadIdentity()
        guard let data = UserDefaults.standard.data(forKey: Self.cacheKey),
              let cached = try? JSONDecoder().decode(CachedSnapshot.self, from: data),
              Date().timeIntervalSince(cached.updatedAt) <= Self.cacheMaxAge else { return }
        snapshot = cached.snapshot
        lastUpdated = cached.updatedAt
    }

    /// Tier chip for the Settings row / strip ("FREE", "PAID", or Google's
    /// own paid-tier name uppercased).
    var tierBadge: String? {
        snapshot?.tierLabel?.uppercased()
    }

    func kickRefresh() {
        guard !AppEnvironment.isDemo,
              detection == .oauthPersonal,
              ProviderVisibilityStore.shared.antigravityPanelShown,
              !loading else { return }
        if let last = lastAttempt, Date().timeIntervalSince(last) < Self.minAttemptGap { return }
        lastAttempt = Date()
        loading = true
        Task { [weak self] in
            let outcome = await AntigravityUsageFetcher.fetch()
            self?.apply(outcome)
        }
    }

    private func apply(_ outcome: AntigravityUsageFetcher.Outcome) {
        loading = false
        switch outcome {
        case .success(let fresh):
            snapshot = fresh
            statusCaption = nil
            lastUpdated = Date()
            loadIdentity()
            persist(fresh)
        case .reauthRequired:
            statusCaption = L10n.tr("sign in again — run agy")
        case .needsCLIInstall:
            statusCaption = L10n.tr("needs a local gemini-cli install")
        case .migratedToAntigravity:
            // A verdict about the account, not a fetch error — drop any
            // stale numbers so the strip doesn't imply a live quota.
            snapshot = nil
            statusCaption = L10n.tr("personal accounts moved to Antigravity — support coming in a later version")
        case .unsupportedAuth:
            snapshot = nil
            statusCaption = L10n.tr("this sign-in method isn't supported yet")
        case .failed(let message):
            // Keep the last good numbers; the caption admits staleness.
            statusCaption = message
        case .notInstalled:
            snapshot = nil
            statusCaption = nil
        }
    }

    private func loadIdentity() {
        guard let creds = AntigravityCredentials.loadCreds(from: AntigravityCredentials.credsURL()) else {
            accountEmail = nil
            return
        }
        accountEmail = creds.email
    }

    private func persist(_ fresh: AntigravityQuotaSnapshot) {
        let cached = CachedSnapshot(snapshot: fresh, updatedAt: Date())
        guard let data = try? JSONEncoder().encode(cached) else { return }
        UserDefaults.standard.set(data, forKey: Self.cacheKey)
    }
}

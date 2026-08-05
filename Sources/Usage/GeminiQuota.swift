import Foundation

/// One `retrieveUserQuota` bucket, normalized to the app's used-percent
/// vocabulary (the endpoint reports `remainingFraction`).
struct GeminiModelBucket: Codable, Equatable {
    var modelId: String
    /// 0...1 consumed.
    var usedPercent: Double
    var resetAt: Date?

    var isFlash: Bool { modelId.lowercased().contains("flash") }
    var isPro: Bool {
        let lowered = modelId.lowercased()
        return lowered.contains("pro") && !lowered.contains("flash")
    }
}

/// What the island renders for Gemini: the Pro-family bucket closest to its
/// limit as the main bar, Flash as the secondary, plus identity garnish.
/// Codable so the last good values survive a relaunch via the cache.
struct GeminiQuotaSnapshot: Codable, Equatable {
    var buckets: [GeminiModelBucket]
    /// Raw tier id from loadCodeAssist ("free-tier", "standard-tier"…).
    var tierID: String?
    /// Display label — paidTier.name when Google provides one, else a
    /// mapping of the tier id.
    var tierLabel: String?

    /// Pro family, lowest remaining (= highest used) wins the main bar.
    var primaryPro: GeminiModelBucket? {
        buckets.filter(\.isPro).max { $0.usedPercent < $1.usedPercent }
    }

    /// Flash family, same lowest-remaining rule, for the secondary caption.
    var secondaryFlash: GeminiModelBucket? {
        buckets.filter(\.isFlash).max { $0.usedPercent < $1.usedPercent }
    }
}

/// Decoders for the two `cloudcode-pa.googleapis.com/v1internal` payloads.
/// JSONSerialization-shaped like the other fetchers — absence is data here
/// too (an account can legitimately report zero buckets).
enum GeminiQuotaParser {
    struct CodeAssistProfile: Equatable {
        var tierID: String?
        var tierLabel: String?
        var projectID: String?
    }

    /// `POST v1internal:loadCodeAssist` — currentTier.id + the quota
    /// project. Google's own paid-tier name wins the label when present.
    static func parseLoadCodeAssist(_ data: Data) -> CodeAssistProfile? {
        guard let root = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else {
            return nil
        }
        let currentTier = root["currentTier"] as? [String: Any]
        let tierID = nonEmpty(currentTier?["id"])
        let paidTier = root["paidTier"] as? [String: Any]
        let label = nonEmpty(paidTier?["name"]) ?? tierLabel(forTierID: tierID)
        return CodeAssistProfile(
            tierID: tierID,
            tierLabel: label,
            projectID: nonEmpty(root["cloudaicompanionProject"])
        )
    }

    /// `POST v1internal:retrieveUserQuota` — buckets[{modelId,
    /// remainingFraction, resetTime}]. An empty/missing buckets array is a
    /// valid answer (fresh account), distinct from a decode failure.
    static func parseQuota(_ data: Data) -> [GeminiModelBucket]? {
        guard let root = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else {
            return nil
        }
        guard let rows = root["buckets"] as? [[String: Any]] else { return [] }
        return rows.compactMap { row in
            guard let modelId = nonEmpty(row["modelId"]) else { return nil }
            let remaining = number(row["remainingFraction"]) ?? 1
            return GeminiModelBucket(
                modelId: modelId,
                usedPercent: min(1, max(0, 1 - remaining)),
                resetAt: timestamp(row["resetTime"])
            )
        }
    }

    /// Google's June-2026 consumer shutdown answers with one of these
    /// instead of data. It's a account-state verdict, not an error.
    static func isMigrationSignal(_ data: Data) -> Bool {
        guard let text = String(data: data, encoding: .utf8) else { return false }
        let lowered = text.lowercased()
        return lowered.contains("unsupported_client")
            || lowered.contains("ineligibletier")
            || lowered.contains("antigravity")
    }

    static func tierLabel(forTierID tierID: String?) -> String? {
        switch tierID {
        case "standard-tier", "g1-pro-tier": return "Paid"
        case "free-tier": return "Free"
        case "legacy-tier": return "Legacy"
        default: return nil
        }
    }

    private static func nonEmpty(_ value: Any?) -> String? {
        guard let raw = value as? String, !raw.isEmpty else { return nil }
        return raw
    }

    private static func number(_ value: Any?) -> Double? {
        if let d = value as? Double { return d }
        if let i = value as? Int { return Double(i) }
        if let s = value as? String { return Double(s) }
        return nil
    }

    private static func timestamp(_ value: Any?) -> Date? {
        guard let raw = value as? String else { return nil }
        return GrokTimestamp.parse(raw)
    }
}

import Foundation

/// Token-tier ladder for the in-card milestone caption. Pure lookup — the
/// recognition lives INSIDE the weekly/monthly cards as one line with an
/// emoji (owner's call, 2026-07-14), not as any separate popup.
enum MilestoneLadder {
    struct Tier {
        let threshold: Int
        let titleKey: String
    }

    static let tokenTiers: [Tier] = [
        Tier(threshold: 100_000_000, titleKey: "First 100M"),
        Tier(threshold: 1_000_000_000, titleKey: "The 1B Club"),
        Tier(threshold: 5_000_000_000, titleKey: "5B Deep"),
        Tier(threshold: 10_000_000_000, titleKey: "The 10B Club"),
        Tier(threshold: 50_000_000_000, titleKey: "The 50B Club"),
        Tier(threshold: 100_000_000_000, titleKey: "The 100B Legend"),
    ]

    /// Highest tier the lifetime total has crossed, nil below the first.
    static func tokenTier(lifetime: Int) -> Tier? {
        tokenTiers.last { lifetime >= $0.threshold }
    }
}

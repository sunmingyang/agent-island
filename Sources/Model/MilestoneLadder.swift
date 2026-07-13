import Foundation

/// The rank ladder for the in-card milestone caption — 方案 A "岛民史诗":
/// one person drifts to the island, takes root, rules, then sails beyond.
/// One coherent story from wave to crown; the top two tiers wear the
/// diamond and the crown (owner's call — prestige metals cap the ladder,
/// 王者荣耀-style). Every share teaches the brand name.
enum MilestoneLadder {
    struct Tier {
        let threshold: Int
        let nameKey: String   // L10n key for the rank name
        let emoji: String
    }

    // Coarse, round thresholds only (owner's call: no fine-grained cuts) —
    // 1亿 / 10亿 / 50亿 / 100亿 / 200亿 / 500亿 / 1000亿.
    static let tokenTiers: [Tier] = [
        Tier(threshold: 100_000_000, nameKey: "Drifter", emoji: "🌊"),
        Tier(threshold: 1_000_000_000, nameKey: "Islander", emoji: "🏝️"),
        Tier(threshold: 5_000_000_000, nameKey: "Settler", emoji: "⛺"),
        Tier(threshold: 10_000_000_000, nameKey: "Chieftain", emoji: "🗿"),
        Tier(threshold: 20_000_000_000, nameKey: "Island Lord", emoji: "🏰"),
        Tier(threshold: 50_000_000_000, nameKey: "Archipelago King", emoji: "💎"),
        Tier(threshold: 100_000_000_000, nameKey: "Legendary Navigator", emoji: "👑"),
    ]

    /// Highest tier the lifetime total has crossed, nil below the first.
    static func tokenTier(lifetime: Int) -> Tier? {
        tokenTiers.last { lifetime >= $0.threshold }
    }
}

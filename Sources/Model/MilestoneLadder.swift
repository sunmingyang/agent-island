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

    static let tokenTiers: [Tier] = [
        Tier(threshold: 10_000_000, nameKey: "Drifter", emoji: "🌊"),
        Tier(threshold: 100_000_000, nameKey: "Islander", emoji: "🏝️"),
        Tier(threshold: 500_000_000, nameKey: "Settler", emoji: "⛺"),
        Tier(threshold: 2_000_000_000, nameKey: "Pioneer", emoji: "🛖"),
        Tier(threshold: 6_000_000_000, nameKey: "Chieftain", emoji: "🗿"),
        Tier(threshold: 15_000_000_000, nameKey: "Island Lord", emoji: "🏰"),
        Tier(threshold: 40_000_000_000, nameKey: "Archipelago King", emoji: "💎"),
        Tier(threshold: 100_000_000_000, nameKey: "Legendary Navigator", emoji: "👑"),
    ]

    /// Highest tier the lifetime total has crossed, nil below the first.
    static func tokenTier(lifetime: Int) -> Tier? {
        tokenTiers.last { lifetime >= $0.threshold }
    }
}

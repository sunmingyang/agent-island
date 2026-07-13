namespace AgentIsland.Model;

/// The rank ladder for the in-card milestone caption — 方案 A "岛民史诗":
/// one person drifts to the island, takes root, rules, then sails beyond.
/// One coherent story from wave to crown; the top two tiers wear the
/// diamond and the crown (owner's call — prestige metals cap the ladder,
/// 王者荣耀-style). Every share teaches the brand name.
public static class MilestoneLadder
{
    public sealed record Tier(long Threshold, string NameKey, string Emoji);

    // Coarse, round thresholds only (owner's call: no fine-grained cuts) —
    // 1亿 / 10亿 / 50亿 / 100亿 / 200亿 / 500亿 / 1000亿.
    public static readonly IReadOnlyList<Tier> TokenTiers = new[]
    {
        new Tier(100_000_000, "Drifter", "🌊"),
        new Tier(1_000_000_000, "Islander", "🏝️"),
        new Tier(5_000_000_000, "Settler", "⛺"),
        new Tier(10_000_000_000, "Chieftain", "🗿"),
        new Tier(20_000_000_000, "Island Lord", "🏰"),
        new Tier(50_000_000_000, "Archipelago King", "💎"),
        new Tier(100_000_000_000, "Legendary Navigator", "👑"),
    };

    /// Highest tier the lifetime total has crossed, null below the first.
    public static Tier? TokenTier(long lifetime) =>
        TokenTiers.LastOrDefault(tier => lifetime >= tier.Threshold);
}

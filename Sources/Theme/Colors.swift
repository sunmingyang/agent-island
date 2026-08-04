import SwiftUI

/// Locked color tokens for AgentIsland.
enum IslandColor {
    /// #0047AB — loading sweep, glow halo.
    static let cobalt = Color(red: 0/255, green: 71/255, blue: 171/255)

    /// #CC785C — Anthropic terracotta. Claude logo + ring/bar fills.
    static let claude = Color(red: 204/255, green: 120/255, blue: 92/255)

    /// #5AA8F0 — OpenAI sky blue. Codex logo + ring/bar fills.
    static let codex = Color(red: 90/255, green: 168/255, blue: 240/255)

    /// #D8DEE4 — xAI monochrome. Grok strip fill + settings dot; a cool
    /// near-white so it sits beside terracotta and sky blue without
    /// introducing a fourth data hue.
    static let grok = Color(red: 216/255, green: 222/255, blue: 228/255)

    /// #3DD68C — live status dot. Sits next to cobalt without clashing.
    static let liveTeal = Color(red: 61/255, green: 214/255, blue: 140/255)

    /// #20C0B0 — the Agent Island logo teal (site logo/favicon color).
    /// Brand-owned surfaces only (e.g. the reset-card face), never
    /// provider data.
    static let brandTeal = Color(red: 32/255, green: 192/255, blue: 176/255)

    /// #8A63FF — glow-color option "Violet". Ambient glow only, never data.
    static let glowViolet = Color(red: 138/255, green: 99/255, blue: 255/255)

    /// #C7D3DF — glow-color option "Silver". A cool near-white; reads as a
    /// moonlit rim on the black silhouette. Ambient glow only, never data.
    static let glowSilver = Color(red: 199/255, green: 211/255, blue: 223/255)

    /// #F5A524 — approaching-limit warning tint. Reads as "amber" against
    /// the black silhouette without competing with the cobalt halo. Used
    /// for the static glow + peek pill accent at warning severity.
    static let alertAmber = Color(red: 245/255, green: 165/255, blue: 36/255)

    /// #E5484D — approaching-limit critical tint. Saturated enough to read
    /// as "stop, you're cooked" without going full red-alert pure.
    static let alertRed = Color(red: 229/255, green: 72/255, blue: 77/255)
}

/// Shared chrome for the floating card windows (report cards, turn alarms).
/// One radius, one base coat — they must read as one family; 26 vs 18 with
/// two different blacks was the "your two popups don't even match" review
/// finding.
enum CardWindow {
    /// Continuous-corner radius for every floating card window.
    static let cornerRadius: CGFloat = 26

    /// #0E0F13 — near-black with a breath of luminance. A flat #000 window
    /// reads as a hole next to the report cards' lifted base.
    static let base = Color(red: 14/255, green: 15/255, blue: 19/255)
}

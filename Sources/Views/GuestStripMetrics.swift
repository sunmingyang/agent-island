import CoreGraphics

/// Shared geometry for the stacked guest strips (usage + cost pages): every
/// row puts its identity in the same fixed leading column and its meter in
/// the same fixed column, so the stack reads as a table instead of a ragged
/// collage. Sized for the widest identity — Antigravity's mark, name, tier
/// and product badges.
enum GuestStripMetrics {
    /// Identity column: mark + name + badges + pool label (usage), mark +
    /// name (cost).
    static let leadingWidth: CGFloat = 220
    /// Quota meter column — identical track width on every row.
    static let meterWidth: CGFloat = 150
}

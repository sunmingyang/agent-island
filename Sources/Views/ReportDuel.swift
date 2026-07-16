import SwiftUI
import AppKit

/// The report cards' faction duel — v3's signature block (locked 2026-07-17).
/// Official provider marks anchor the two ends of a split beam; the clash
/// spark sits exactly at the usage-share split, and the chibi duel artwork
/// stands over it. Three states, picked from the share alone:
///   Claude ≥52% → orange side wins (crowned, stomping)
///   Codex  ≥52% → blue side wins (pre-mirrored asset so the winner
///                  charges in from Codex's right end)
///   48–52%      → back-to-back draw
struct ReportDuel: View {
    let claudeShare: Double   // 0...1 of the period's tokens

    private static let claudeMark = Bundle.main.url(forResource: "claude_logo", withExtension: "pdf")
        .flatMap { NSImage(contentsOf: $0) }
    private static let codexMark = Bundle.main.url(forResource: "openai_logo", withExtension: "pdf")
        .flatMap { NSImage(contentsOf: $0) }

    private static func duelArt(claudeShare: Double) -> NSImage? {
        let name: String
        if claudeShare >= 0.52 { name = "duel-claude-wins" }
        else if claudeShare <= 0.48 { name = "duel-codex-wins" }
        else { name = "duel-draw" }
        return Bundle.main.url(forResource: name, withExtension: "png")
            .flatMap { NSImage(contentsOf: $0) }
    }

    private static let artHeight: CGFloat = 82
    private static let markSide: CGFloat = 18
    private static let markGap: CGFloat = 10
    private static let beamHeight: CGFloat = 6

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            GeometryReader { geo in
                let beamX0 = Self.markSide + Self.markGap
                let beamW = max(1, geo.size.width - 2 * (Self.markSide + Self.markGap))
                let share = CGFloat(min(1, max(0, claudeShare)))
                // The spark rides the TRUE split; only the artwork clamps
                // inward so a 90/10 blowout doesn't shove it off the card.
                let sparkX = beamX0 + beamW * min(0.97, max(0.03, share))
                let artX = beamX0 + beamW * min(0.74, max(0.26, share))
                let beamY = Self.artHeight + 10

                ZStack(alignment: .topLeading) {
                    if let art = Self.duelArt(claudeShare: claudeShare) {
                        Image(nsImage: art)
                            .resizable()
                            .aspectRatio(contentMode: .fit)
                            .frame(height: Self.artHeight)
                            .position(x: artX, y: Self.artHeight / 2 + 1)
                    }

                    mark(Self.claudeMark, tint: IslandColor.claude)
                        .position(x: Self.markSide / 2, y: beamY)
                    mark(Self.codexMark, tint: IslandColor.codex)
                        .position(x: geo.size.width - Self.markSide / 2, y: beamY)

                    HStack(spacing: 1.5) {
                        Capsule()
                            .fill(LinearGradient(
                                colors: [Color(red: 0.88, green: 0.54, blue: 0.39), IslandColor.claude],
                                startPoint: .leading, endPoint: .trailing))
                            .frame(width: max(3, beamW * share))
                        Capsule()
                            .fill(LinearGradient(
                                colors: [IslandColor.codex, Color(red: 0.50, green: 0.74, blue: 0.96)],
                                startPoint: .leading, endPoint: .trailing))
                    }
                    .frame(width: beamW, height: Self.beamHeight)
                    .position(x: beamX0 + beamW / 2, y: beamY)

                    clashSpark
                        .position(x: sparkX, y: beamY)
                }
            }
            .frame(height: Self.artHeight + 20)

            HStack {
                legendTag(name: "Claude", pct: claudeShare, color: IslandColor.claude)
                Spacer()
                legendTag(name: "Codex", pct: 1 - claudeShare, color: IslandColor.codex)
            }
        }
    }

    private func mark(_ image: NSImage?, tint: Color) -> some View {
        Group {
            if let image {
                Image(nsImage: image)
                    .resizable()
                    .renderingMode(.template)
                    .aspectRatio(contentMode: .fit)
                    .foregroundStyle(tint)
            } else {
                Circle().fill(tint)
            }
        }
        .frame(width: Self.markSide, height: Self.markSide)
    }

    /// White core + four-point star, warm glow to the Claude side and cool
    /// to the Codex side — the "swords meet here" moment.
    private var clashSpark: some View {
        ZStack {
            ForEach(0..<2, id: \.self) { i in
                Capsule()
                    .fill(.white)
                    .frame(width: 1.6, height: 17)
                    .rotationEffect(.degrees(Double(i) * 90 + 14))
            }
            .shadow(color: .white.opacity(0.9), radius: 2.5)
            Circle()
                .fill(.white)
                .frame(width: 7, height: 7)
                .shadow(color: .white.opacity(0.95), radius: 5)
                .shadow(color: IslandColor.claude.opacity(0.8), radius: 7, x: -5)
                .shadow(color: IslandColor.codex.opacity(0.8), radius: 7, x: 5)
        }
    }

    private func legendTag(name: String, pct: Double, color: Color) -> some View {
        HStack(spacing: 6) {
            Circle().fill(color).frame(width: 7, height: 7)
            Text(name)
                .font(.system(size: 11.5, weight: .bold, design: .rounded))
                .foregroundStyle(.white.opacity(0.8))
            Text("\(Int((pct * 100).rounded()))%")
                .font(.system(size: 11.5, weight: .heavy, design: .rounded))
                .monospacedDigit()
                .foregroundStyle(color)
        }
    }
}

/// The v3 rank block — bottom of both cards. Line one: lifetime tokens in
/// plain white with bold figures. Line two: the congratulation, rank name
/// oversized in achievement gold. No plate, no divider (owner: 金框很丑).
struct ReportRankBlock: View {
    let lifetimeText: String
    let tierEmoji: String?
    let tierName: String?

    private static let gold = Color(red: 0.89, green: 0.70, blue: 0.31)

    var body: some View {
        let zh = L10n.locale.identifier.hasPrefix("zh")
        VStack(spacing: 7) {
            Text(L10n.tr("lifetime %@ tokens", lifetimeText))
                .font(.system(size: 12, weight: .semibold, design: .rounded))
                .monospacedDigit()
                .foregroundStyle(.white.opacity(0.62))
            if let tierName, let tierEmoji {
                (Text(tierEmoji + " ")
                    + Text(L10n.tr("Congrats, you've reached "))
                    + Text(L10n.tr(tierName))
                        .font(.system(size: zh ? 23 : 19, weight: .black, design: .rounded))
                    + Text(L10n.tr(" rank")))
                    .font(.system(size: 14, weight: .heavy, design: .rounded))
                    .foregroundStyle(Self.gold)
            }
        }
        .frame(maxWidth: .infinity)
    }
}

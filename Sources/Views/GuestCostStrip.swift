import SwiftUI
import AppKit

/// One-row dollars readout under the Claude/Codex cost columns for a guest
/// provider — signed in on this machine but outside the two island slots.
/// Today's spend and the calendar month to date, in the same 30pt strip
/// shape the usage page gives the guests' quota, so both pages keep the same
/// height while swiping.
///
/// A guest with no local ledger shows "No local cost data" rather than a
/// fabricated $0 (repo honesty rule); a guest whose number never priced
/// (Cursor without a priced model) still shows its dollars as computed.
struct GuestCostStrip: View {
    let provider: DisplayProvider

    @ObservedObject private var store = CostStore.shared
    @State private var hovered = false

    private var cost: ProviderCost { store.cost(for: provider) }
    private var loading: Bool { store.isLoading(provider) }

    var body: some View {
        HStack(spacing: 10) {
            HStack(spacing: 6) {
                ProviderMark(provider: provider, size: 13, tint: provider.brandColor)
                Text(provider.displayName)
                    .font(Typography.providerTitle)
                    .foregroundStyle(.white.opacity(0.88))
            }
            .frame(width: GuestStripMetrics.leadingWidth, alignment: .leading)

            if hasLedger {
                figure(label: cost.today.label, dollars: cost.today.dollars)
                figure(label: cost.month.label, dollars: cost.month.dollars)
            } else if loading {
                Text(L10n.tr("Syncing…"))
                    .font(Typography.label)
                    .foregroundStyle(.white.opacity(0.36))
            } else {
                Text(L10n.tr("No local cost data"))
                    .font(Typography.label)
                    .foregroundStyle(.white.opacity(0.45))
            }

            Spacer(minLength: 8)
        }
        .padding(.horizontal, 12)
        .frame(maxWidth: .infinity)
        .frame(height: 30)
        .background(
            RoundedRectangle(cornerRadius: 8)
                .fill(.white.opacity(hovered ? 0.055 : 0))
        )
        .overlay(alignment: .bottomLeading) {
            if hovered {
                hoverCard
                    .offset(x: 8, y: -36)
                    .transition(.opacity)
            }
        }
        .contentShape(RoundedRectangle(cornerRadius: 8))
        .onHover { hovered = $0 }
        .onTapGesture {
            guard let url = Self.billingURL(for: provider) else { return }
            NSWorkspace.shared.open(url)
        }
        .help(detailTooltip)
        .accessibilityElement(children: .combine)
        .accessibilityLabel(accessibilitySummary)
        .accessibilityHint(L10n.tr("Open %@ usage", provider.displayName))
        .zIndex(hovered ? 20 : 0)
        .animation(.easeOut(duration: 0.12), value: hovered)
    }

    private func figure(label: String, dollars: Double) -> some View {
        HStack(spacing: 4) {
            Text(L10n.tr(label))
                .font(Typography.micro)
                .foregroundStyle(.white.opacity(0.40))
            Text(Self.formatDollars(dollars))
                .font(Typography.bodyNumber)
                .foregroundStyle(.white.opacity(0.88))
        }
    }

    /// Same ledger test as `CostView.hasLedger` — any token or dollar figure
    /// on record. Token-inclusive so a Cursor install that moved tokens
    /// keeps its row even when no model priced.
    private var hasLedger: Bool {
        cost.today.tokens > 0 || cost.month.tokens > 0
            || cost.today.dollars > 0 || cost.month.dollars > 0
            || cost.dailyTokens.contains { $0.tokens > 0 }
    }

    /// Cents stay visible through $99 — guest ledgers are small numbers and
    /// "$28.40" should not read as "$28"; above that the change is noise
    /// (the same rounding the cost tiles apply).
    private static func formatDollars(_ v: Double) -> String {
        v < 100 ? String(format: "$%.2f", v) : String(format: "$%.0f", v)
    }

    /// The provider's own usage/billing surface — the same destinations the
    /// usage strips open.
    private static func billingURL(for provider: DisplayProvider) -> URL? {
        switch provider {
        case .grok: return URL(string: "https://grok.com")
        case .antigravity: return URL(string: "https://antigravity.google")
        case .cursor: return URL(string: "https://cursor.com/dashboard")
        case .claude, .codex: return nil
        }
    }

    private var hoverCard: some View {
        Text(detailTooltip)
            .font(.system(size: 9.5, weight: .semibold, design: .rounded))
            .foregroundStyle(.white.opacity(0.88))
            .lineSpacing(2)
            .frame(width: 300, alignment: .leading)
            .fixedSize(horizontal: false, vertical: true)
            .padding(.horizontal, 10)
            .padding(.vertical, 8)
            .background(
                RoundedRectangle(cornerRadius: 9, style: .continuous)
                    .fill(Color(red: 0.035, green: 0.038, blue: 0.048).opacity(0.98))
                    .overlay {
                        RoundedRectangle(cornerRadius: 9, style: .continuous)
                            .strokeBorder(.white.opacity(0.12), lineWidth: 0.5)
                    }
            )
            .shadow(color: .black.opacity(0.45), radius: 10, y: 4)
            .allowsHitTesting(false)
    }

    /// Today + month, then the month's top models by dollars — the strip
    /// answers "how much" and the hover answers "on what".
    private var detailTooltip: String {
        guard hasLedger else { return L10n.tr("No local cost data") }
        var lines = [
            "\(provider.displayName) · \(L10n.tr(cost.today.label)) \(Self.formatDollars(cost.today.dollars))",
            "\(L10n.tr(cost.month.label)) \(Self.formatDollars(cost.month.dollars))",
        ]
        let models = cost.monthByModel.filter { $0.dollars > 0 }.prefix(3)
        for row in models {
            lines.append("\(row.displayName)  \(Self.formatDollars(row.dollars))")
        }
        lines.append(L10n.tr("Click to open %@", provider.displayName))
        return lines.joined(separator: "\n")
    }

    private var accessibilitySummary: String {
        guard hasLedger else {
            return L10n.tr("%@: no local cost data", provider.displayName)
        }
        return L10n.tr(
            "%@: %@ today, %@ this month",
            provider.displayName,
            Self.formatDollars(cost.today.dollars),
            Self.formatDollars(cost.month.dollars)
        )
    }
}

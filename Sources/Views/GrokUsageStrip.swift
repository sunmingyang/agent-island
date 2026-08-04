import SwiftUI

/// One-row Grok readout under the Claude/Codex tiles on the usage page:
/// weekly credit-pool meter (the number SuperGrok users budget around)
/// with its reset countdown, plus the monthly dollar budget on the right.
/// Deliberately a strip, not a third tile column — Grok is a guest on the
/// island until the 1.9 multi-provider layout, and the fixed-height tile
/// row has no third slot to give.
struct GrokUsageStrip: View {
    @ObservedObject private var store = GrokUsageStore.shared
    @ObservedObject private var quotaMode = QuotaDisplayModeStore.shared

    var body: some View {
        HStack(spacing: 10) {
            HStack(spacing: 6) {
                Circle()
                    .fill(IslandColor.grok)
                    .frame(width: 5, height: 5)
                    .accessibilityHidden(true)
                Text("Grok")
                    .font(Typography.providerTitle)
                    .foregroundStyle(.white.opacity(0.88))
                Text(L10n.tr("week"))
                    .font(Typography.micro)
                    .foregroundStyle(.white.opacity(0.40))
            }

            if let snapshot = store.snapshot {
                let value = quotaMode.displayValue(usedPercent: snapshot.weeklyUsedPercent)
                meter(fraction: value / 100)
                Text("\(Int(value.rounded()))%")
                    .font(Typography.bodyNumber)
                    .foregroundStyle(.white.opacity(0.88))
                Text(caption(for: snapshot))
                    .font(Typography.label)
                    .foregroundStyle(.white.opacity(store.errorCaption == nil ? 0.45 : 0.55))
                    .lineLimit(1)
                Spacer(minLength: 8)
                if let used = snapshot.monthlyUsedCents, let limit = snapshot.monthlyLimitCents {
                    Text(L10n.tr(
                        "$%@ / $%@ monthly",
                        GrokBillingParser.dollars(fromCents: used),
                        GrokBillingParser.dollars(fromCents: limit)
                    ))
                    .font(Typography.label)
                    .foregroundStyle(.white.opacity(0.50))
                    .lineLimit(1)
                }
            } else if let caption = store.errorCaption {
                Text(caption)
                    .font(Typography.label)
                    .foregroundStyle(.white.opacity(0.55))
                    .lineLimit(1)
                Spacer(minLength: 8)
            } else {
                Text(L10n.tr("Syncing…"))
                    .font(Typography.label)
                    .foregroundStyle(.white.opacity(0.36))
                Spacer(minLength: 8)
            }
        }
        .padding(.horizontal, 12)
        .frame(maxWidth: .infinity)
        .frame(height: 30)
        .accessibilityElement(children: .combine)
        .accessibilityLabel(accessibilitySummary)
    }

    private func meter(fraction: Double) -> some View {
        GeometryReader { geo in
            ZStack(alignment: .leading) {
                Capsule().fill(.white.opacity(0.07))
                Capsule()
                    .fill(IslandColor.grok.opacity(0.85))
                    .frame(width: max(0, geo.size.width * min(1, max(0, fraction))))
            }
        }
        .frame(width: 150, height: 5)
    }

    /// Errors replace the reset countdown, same slot policy as the tiles —
    /// preserved stale values may carry an old resetAt that would
    /// otherwise render as a fake fresh countdown.
    private func caption(for snapshot: GrokBillingSnapshot) -> String {
        if let error = store.errorCaption { return error }
        guard let end = snapshot.weeklyPeriodEnd else { return "" }
        return L10n.tr("resets in %@", Duration.compact(max(0, end.timeIntervalSinceNow)))
    }

    private var accessibilitySummary: String {
        guard let snapshot = store.snapshot else {
            return store.errorCaption ?? L10n.tr("Weekly Grok limit")
        }
        let percent = Int((snapshot.weeklyUsedPercent * 100).rounded())
        return L10n.tr("%@: %d percent of weekly window used", L10n.tr("Weekly Grok limit"), percent)
    }
}

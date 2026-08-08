import SwiftUI
import AppKit

struct GeminiUsageStrip: View {
    @ObservedObject private var store = GeminiUsageStore.shared
    @ObservedObject private var quotaMode = QuotaDisplayModeStore.shared
    @State private var hovered = false

    private static let quotaURL = URL(string: "https://developers.google.com/gemini-code-assist/resources/quotas")

    var body: some View {
        HStack(spacing: 10) {
            HStack(spacing: 6) {
                ProviderMark(provider: .gemini, size: 13, tint: IslandColor.gemini)
                Text("Gemini")
                    .font(Typography.providerTitle)
                    .foregroundStyle(.white.opacity(0.88))
                if let tier = store.tierBadge {
                    Text(tier)
                        .font(Typography.micro)
                        .foregroundStyle(.white.opacity(0.40))
                }
            }

            if let bucket = store.snapshot?.primaryPro {
                Text(L10n.tr("Pro"))
                    .font(Typography.micro)
                    .foregroundStyle(.white.opacity(0.40))
                let value = quotaMode.displayValue(usedPercent: bucket.usedPercent)
                meter(fraction: value / 100)
                Text("\(Int(value.rounded()))%")
                    .font(Typography.bodyNumber)
                    .foregroundStyle(.white.opacity(0.88))
                Text(caption(for: bucket))
                    .font(Typography.label)
                    .foregroundStyle(.white.opacity(store.statusCaption == nil ? 0.45 : 0.55))
                    .lineLimit(1)
            } else if let caption = store.statusCaption {
                Text(caption)
                    .font(Typography.label)
                    .foregroundStyle(.white.opacity(0.55))
                    .lineLimit(1)
            } else {
                Text(L10n.tr("Syncing…"))
                    .font(Typography.label)
                    .foregroundStyle(.white.opacity(0.36))
            }

            Spacer(minLength: 8)

            if let flash = store.snapshot?.secondaryFlash {
                let value = quotaMode.displayValue(usedPercent: flash.usedPercent)
                Text(L10n.tr("flash %d%%", Int(value.rounded())))
                    .font(Typography.label)
                    .foregroundStyle(.white.opacity(0.50))
                    .lineLimit(1)
            }
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
            guard let url = Self.quotaURL else { return }
            NSWorkspace.shared.open(url)
        }
        .help(detailTooltip)
        .accessibilityElement(children: .combine)
        .accessibilityLabel(accessibilitySummary)
        .accessibilityHint(L10n.tr("Open Gemini quota details"))
        .zIndex(hovered ? 20 : 0)
        .animation(.easeOut(duration: 0.12), value: hovered)
    }

    private var hoverCard: some View {
        Text(detailTooltip)
            .font(.system(size: 9.5, weight: .semibold, design: .rounded))
            .foregroundStyle(.white.opacity(0.88))
            .lineSpacing(2)
            .frame(width: 340, alignment: .leading)
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

    private func meter(fraction: Double) -> some View {
        GeometryReader { geo in
            ZStack(alignment: .leading) {
                Capsule().fill(.white.opacity(0.07))
                Capsule()
                    .fill(IslandColor.gemini.opacity(0.85))
                    .frame(width: max(0, geo.size.width * min(1, max(0, fraction))))
            }
        }
        .frame(width: 132, height: 5)
    }

    private func caption(for bucket: GeminiModelBucket) -> String {
        if let status = store.statusCaption { return status }
        guard let resetAt = bucket.resetAt,
              resetAt.timeIntervalSinceNow > 0 else { return "" }
        return L10n.tr("resets in %@", Duration.compact(resetAt.timeIntervalSinceNow))
    }

    private var detailTooltip: String {
        var lines = [L10n.tr("Gemini quota by model")]
        if let email = store.accountEmail { lines.append(email) }
        if let status = store.statusCaption { lines.append("⚠ \(status)") }
        let buckets = store.snapshot?.buckets.sorted { $0.modelId < $1.modelId } ?? []
        for bucket in buckets {
            let value = Int(quotaMode.displayValue(usedPercent: bucket.usedPercent).rounded())
            var line = "\(bucket.modelId): \(value)%"
            if let resetAt = bucket.resetAt {
                line += " · \(L10n.tr("resets in %@", Duration.compact(max(0, resetAt.timeIntervalSinceNow))))"
            }
            lines.append(line)
        }
        lines.append(L10n.tr("Click to open official quota documentation"))
        return lines.joined(separator: "\n")
    }

    private var accessibilitySummary: String {
        guard let bucket = store.snapshot?.primaryPro else {
            return store.statusCaption ?? L10n.tr("Gemini quota")
        }
        let percent = Int((bucket.usedPercent * 100).rounded())
        return L10n.tr("%@: %d percent used", L10n.tr("Gemini Pro quota"), percent)
    }
}

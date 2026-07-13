import SwiftUI

/// Provider titles row — Claude on the left, Codex on the right, with a
/// notch-width spacer in the middle that hides the title content behind
/// the physical notch. Lives outside `PagedContent` so it stays fixed
/// while the data area swipes between usage/cost/overview screens.
///
/// Plan tags ("MAX" / "PLUS") are sourced from `UsageStore` since the
/// subscription tier is a property of the account, not the current page.
struct PanelHeader: View {
    let notch: NotchInfo
    @ObservedObject private var visibility = ProviderVisibilityStore.shared
    @ObservedObject private var usageStore = UsageStore.shared

    var body: some View {
        HStack(spacing: 0) {
            let claudeOn = visibility.claudeVisible
            let codexOn = visibility.codexVisible
            providerTitle(name: "Claude", tag: usageStore.claude.plan?.uppercased(),
                          color: IslandColor.claude, alignment: .leading)
                .opacity(claudeOn ? 1 : 0)
                .animation(.openMorph, value: claudeOn)
                .accessibilityHidden(!claudeOn)
            Color.clear.frame(width: notch.width)
            HStack(spacing: 0) {
                Spacer(minLength: 0)
                // Banked-reset count ("reset cards") — the escape hatches of
                // the weekly-only quota era, read straight from the usage
                // payload (rate_limit_reset_credits.available_count). Always
                // shown, ×0 included, in the dead space left of the title.
                resetCardChip(usageStore.codex.resetCards ?? 0)
                    .padding(.trailing, 10)
                providerTitle(name: "Codex", tag: usageStore.codex.plan?.uppercased(),
                              color: IslandColor.codex, alignment: .trailing)
                    .fixedSize()
            }
            .frame(maxWidth: .infinity)
            .opacity(codexOn ? 1 : 0)
            .animation(.openMorph, value: codexOn)
            .accessibilityHidden(!codexOn)
        }
        .frame(height: 22)
        .padding(.horizontal, 16)
        .padding(.top, 4)
        .padding(.bottom, min(14, max(0, notch.height - 22 - 4)))
    }

    /// Banked Codex resets, drawn as a tiny physical CARD with the OpenAI
    /// mark printed on it, and "×N" beside it — no colored container box.
    /// The card reads as an object (gradient face, top-edge catchlight,
    /// drop shadow), which is the whole point: these are cards you hold.
    private func resetCardChip(_ count: Int) -> some View {
        HStack(spacing: 6) {
            ZStack {
                RoundedRectangle(cornerRadius: 3.5, style: .continuous)
                    .fill(
                        LinearGradient(
                            colors: [Color(white: 0.22), Color(white: 0.075)],
                            startPoint: .topLeading, endPoint: .bottomTrailing
                        )
                    )
                RoundedRectangle(cornerRadius: 3.5, style: .continuous)
                    .strokeBorder(
                        LinearGradient(
                            colors: [.white.opacity(0.38), .white.opacity(0.04)],
                            startPoint: .top, endPoint: .bottom
                        ),
                        lineWidth: 0.6
                    )
                if let logo = ProviderLogos.openAI {
                    Image(nsImage: logo)
                        .renderingMode(.template)
                        .resizable()
                        .aspectRatio(contentMode: .fit)
                        .frame(height: 9)
                        .foregroundStyle(.white.opacity(count > 0 ? 0.92 : 0.45))
                }
            }
            .frame(width: 25, height: 16)
            .shadow(color: .black.opacity(0.55), radius: 1.6, y: 1)
            Text("×\(count)")
                .font(Typography.chip)
                .tracking(0.6)
                .foregroundStyle(.white.opacity(count > 0 ? 0.78 : 0.42))
        }
        .help(L10n.tr("%d banked resets available", count))
        .accessibilityLabel(L10n.tr("%d banked resets available", count))
    }

    @ViewBuilder
    private func providerTitle(
        name: String,
        tag: String?,
        color: Color,
        alignment: HorizontalAlignment
    ) -> some View {
        // Push past where the overlay logo lands: 9 leading + 20 logo + 8 gap.
        let logoOffset: CGFloat = 9 + 20 + 8

        let content = HStack(spacing: 8) {
            Text(name)
                .font(Typography.providerTitle)
                .foregroundStyle(.white)
            if let tag {
                Text(tag)
                    .font(Typography.chip)
                    .tracking(0.8)
                    .foregroundStyle(.white.opacity(0.6))
                    .padding(.horizontal, 5)
                    .padding(.vertical, 2)
                    .background(
                        RoundedRectangle(cornerRadius: 3)
                            .fill(.white.opacity(0.06))
                            .overlay(
                                RoundedRectangle(cornerRadius: 3)
                                    .strokeBorder(.white.opacity(0.08), lineWidth: 0.5)
                            )
                    )
            }
        }

        if alignment == .leading {
            HStack {
                content.padding(.leading, logoOffset)
                Spacer(minLength: 0)
            }
            .frame(maxWidth: .infinity)
        } else {
            HStack {
                Spacer(minLength: 0)
                content.padding(.trailing, logoOffset)
            }
            .frame(maxWidth: .infinity)
        }
    }
}

import SwiftUI
import AppKit

/// Fills the half of the panel freed when only one provider is subscribed:
/// the provider's mark and name — one quiet identity moment, numbers on the
/// other side (owner's solo-layout call, 2026-07-16). Replaces the inherited
/// per-model breakdown table, whose "5h" legend read as a quota window Codex
/// no longer has.
struct SoloProviderBadge: View {
    let provider: AlertEngine.Provider

    private static let claudeImage = Bundle.main.url(forResource: "claude_logo", withExtension: "pdf")
        .flatMap { NSImage(contentsOf: $0) }
    private static let codexImage = Bundle.main.url(forResource: "openai_logo", withExtension: "pdf")
        .flatMap { NSImage(contentsOf: $0) }

    var body: some View {
        VStack(spacing: 11) {
            Spacer(minLength: 0)
            ZStack {
                Circle()
                    .fill(color.opacity(0.09))
                Circle()
                    .strokeBorder(color.opacity(0.22), lineWidth: 0.5)
                if let image {
                    Image(nsImage: image)
                        .resizable()
                        .renderingMode(.template)
                        .aspectRatio(contentMode: .fit)
                        .foregroundStyle(color.opacity(0.95))
                        .frame(width: 25, height: 25)
                }
            }
            .frame(width: 56, height: 56)
            .shadow(color: color.opacity(0.22), radius: 12)
            Text(name)
                .font(Typography.providerTitle)
                .tracking(0.4)
                .foregroundStyle(.white.opacity(0.88))
            Spacer(minLength: 0)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .accessibilityElement(children: .combine)
        .accessibilityLabel(name)
    }

    private var color: Color {
        provider == .claude ? IslandColor.claude : IslandColor.codex
    }

    private var name: String {
        provider == .claude ? "Claude Code" : "Codex"
    }

    private var image: NSImage? {
        provider == .claude ? Self.claudeImage : Self.codexImage
    }
}

/// Shown on usage and cost pages when the user has toggled both providers
/// off in Settings. Reads as "intentionally quiet" rather than "broken
/// page", and points the user back at the affordance that got them here.
struct BothHiddenPlaceholder: View {
    var body: some View {
        VStack(spacing: 6) {
            Spacer(minLength: 0)
            Text(L10n.tr("Both providers hidden"))
                .font(Typography.providerTitle)
                .foregroundStyle(.white.opacity(0.45))
            Text(L10n.tr("Re-enable in Settings → Providers"))
                .font(Typography.caption)
                .foregroundStyle(.white.opacity(0.32))
            Spacer(minLength: 0)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
    }
}

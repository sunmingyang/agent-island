import AppKit
import SwiftUI

/// The five-blade mark on a faintly lifted tile — the brand book's menu-bar
/// treatment. On a bare dark field the mark's graphite and black blades
/// vanish below ~24pt (owner report, 2026-08-09); the tile restores the
/// silhouette without inventing a background color.
struct BrandMarkTile: View {
    var side: CGFloat = 24
    var corner: CGFloat = 6
    /// Extra rotation applied by the parent (hover spin and the like) —
    /// kept a plain parameter so this view stays stateless.
    var spin: Double = 0

    /// The small-size-optimized variant (bolder blades, lightened
    /// graphite) — generated for exactly this tile; the full mark stays the
    /// fallback so a missing resource degrades to the real logo.
    private static let mark: NSImage? =
        (Bundle.main.url(forResource: "agentisland_logo_small", withExtension: "png")
            ?? Bundle.main.url(forResource: "agentisland_logo", withExtension: "png"))
            .flatMap { NSImage(contentsOf: $0) }

    var body: some View {
        ZStack {
            RoundedRectangle(cornerRadius: corner, style: .continuous)
                .fill(.white.opacity(0.07))
            RoundedRectangle(cornerRadius: corner, style: .continuous)
                .strokeBorder(.white.opacity(0.09), lineWidth: 1)
            if let mark = Self.mark {
                Image(nsImage: mark)
                    .renderingMode(.original)
                    .resizable()
                    .interpolation(.high)
                    .aspectRatio(contentMode: .fit)
                    .frame(width: side * 0.68, height: side * 0.68)
                    .rotationEffect(.degrees(spin))
            }
        }
        .frame(width: side, height: side)
        .accessibilityHidden(true)
    }
}

/// Sidebar variant: hovering clicks the pinwheel one blade over — 72° is
/// exactly one blade of five, so the mark lands back on itself. One spring,
/// GPU rotation only.
struct SidebarBrandMark: View {
    @State private var spin: Double = 0

    var body: some View {
        BrandMarkTile(side: 24, spin: spin)
            .onHover { hovering in
                guard hovering else { return }
                withAnimation(.spring(response: 0.55, dampingFraction: 0.68)) {
                    spin += 72
                }
            }
    }
}

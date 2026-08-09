import AppKit
import SwiftUI

/// The bare five-blade mark — transparent, no plate, no border. The framed
/// tile treatment read as "the app icon pasted into a box" (owner review,
/// 2026-08-09: 应该用没有框着的那个透明的五瓣); with the small-optimized
/// variant and the normalized, centered mark PNGs the bare glyph stays
/// legible at chrome sizes on its own.
struct BrandMark: View {
    var side: CGFloat = 24
    /// Extra rotation applied by the parent (hover spin and the like) —
    /// kept a plain parameter so this view stays stateless.
    var spin: Double = 0

    /// The small-size-optimized variant (bolder blades, lightened
    /// graphite) — the full mark stays the fallback so a missing resource
    /// degrades to the real logo.
    private static let mark: NSImage? =
        (Bundle.main.url(forResource: "agentisland_logo_small", withExtension: "png")
            ?? Bundle.main.url(forResource: "agentisland_logo", withExtension: "png"))
            .flatMap { NSImage(contentsOf: $0) }

    var body: some View {
        Group {
            if let mark = Self.mark {
                Image(nsImage: mark)
                    .renderingMode(.original)
                    .resizable()
                    .interpolation(.high)
                    .aspectRatio(contentMode: .fit)
                    .rotationEffect(.degrees(spin))
            } else {
                Color.clear
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
        BrandMark(side: 24, spin: spin)
            .onHover { hovering in
                guard hovering else { return }
                withAnimation(.spring(response: 0.55, dampingFraction: 0.68)) {
                    spin += 72
                }
            }
    }
}

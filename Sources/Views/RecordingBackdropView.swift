import SwiftUI

/// Fullscreen stage for recording rigs — the same dark, faintly brand-lit
/// texture as the share cards, so raw clips sit on a clean, private,
/// on-brand base instead of whatever the desktop happens to show.
struct RecordingBackdropView: View {
    var body: some View {
        ZStack {
            LinearGradient(
                colors: [Color(red: 0.055, green: 0.06, blue: 0.075),
                         Color(red: 0.02, green: 0.022, blue: 0.03)],
                startPoint: .top, endPoint: .bottom
            )
            RadialGradient(colors: [IslandColor.claude.opacity(0.05), .clear],
                           center: .init(x: 0.15, y: 0.1), startRadius: 0, endRadius: 700)
            RadialGradient(colors: [IslandColor.codex.opacity(0.05), .clear],
                           center: .init(x: 0.85, y: 0.75), startRadius: 0, endRadius: 800)
        }
        .ignoresSafeArea()
    }
}

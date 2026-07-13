import SwiftUI
import AppKit

/// Fullscreen stage for recording rigs. V1 was a card-style gradient (read
/// as a "grey board" — rejected); V2 is a stock macOS wallpaper, so clips
/// look like a clean real desktop while guaranteeing no private windows,
/// desktop widgets, or note titles ever enter frame.
struct RecordingBackdropView: View {
    private static let wallpaper: NSImage? = {
        let candidates = [
            "/System/Library/Desktop Pictures/Sonoma.heic",
            "/System/Library/Desktop Pictures/Radial Sky Blue.heic",
            "/System/Library/Desktop Pictures/Mac Blue.heic",
        ]
        for path in candidates {
            if let image = NSImage(contentsOfFile: path) { return image }
        }
        return nil
    }()

    var body: some View {
        Group {
            if let wallpaper = Self.wallpaper {
                Image(nsImage: wallpaper)
                    .resizable()
                    .aspectRatio(contentMode: .fill)
            } else {
                LinearGradient(
                    colors: [Color(red: 0.055, green: 0.06, blue: 0.075),
                             Color(red: 0.02, green: 0.022, blue: 0.03)],
                    startPoint: .top, endPoint: .bottom
                )
            }
        }
        .ignoresSafeArea()
    }
}

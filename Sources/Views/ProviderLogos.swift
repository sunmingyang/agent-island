import AppKit
import SwiftUI

/// Bundle-shipped provider marks (template PDFs), shared by any view that
/// prints a Claude/OpenAI logo outside the island itself (turn alarm, the
/// reset-card chip, the weekly report card).
enum ProviderLogos {
    static let claude = load("claude_logo")
    static let openAI = load("openai_logo")
    // Official marks, converted to template PDFs from vendor vectors
    // (Cursor: cursor.com mark via PR #36's path; Grok: grok.com slash
    // mark; Gemini: the four-point star). Same nominative-use footing as
    // the Claude/OpenAI marks above.
    static let antigravity = load("antigravity_logo")
    static let grok = load("grok_logo")
    static let cursor = load("cursor_logo")

    private static func load(_ name: String) -> NSImage? {
        guard let url = Bundle.main.url(forResource: name, withExtension: "pdf"),
              let image = NSImage(contentsOf: url) else { return nil }
        return image
    }
}

extension DisplayProvider {
    var brandColor: Color {
        switch self {
        case .claude: return IslandColor.claude
        case .codex: return IslandColor.codex
        case .antigravity: return IslandColor.antigravity
        case .grok: return IslandColor.grok
        case .cursor: return IslandColor.cursor
        }
    }
}

struct ProviderMark: View {
    let provider: DisplayProvider
    let size: CGFloat
    let tint: Color

    var body: some View {
        Group {
            switch provider {
            case .claude:
                imageMark(ProviderLogos.claude)
            case .codex:
                imageMark(ProviderLogos.openAI)
            case .antigravity:
                imageMark(ProviderLogos.antigravity)
            case .grok:
                imageMark(ProviderLogos.grok)
            case .cursor:
                imageMark(ProviderLogos.cursor)
            }
        }
        .foregroundStyle(tint)
        .accessibilityHidden(true)
    }

    @ViewBuilder
    private func imageMark(_ image: NSImage?) -> some View {
        if let image {
            Image(nsImage: image)
                .resizable()
                .renderingMode(.template)
                .aspectRatio(contentMode: .fit)
                .frame(width: size, height: size)
        } else {
            Circle()
                .strokeBorder(tint, lineWidth: 1.5)
                .frame(width: size, height: size)
        }
    }
}

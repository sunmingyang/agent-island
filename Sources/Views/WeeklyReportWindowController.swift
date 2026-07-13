import AppKit
import SwiftUI

/// Renders the weekly report card to a crisp PNG (3x) and hosts the share
/// window. Sharing is always the USER posting an image — nothing leaves
/// the machine on its own.
@MainActor
enum WeeklyReportRenderer {
    /// The EXPORT version is the card itself, full-bleed with SQUARE outer
    /// corners and no backdrop margin: social apps flatten transparency to
    /// white (ugly corner nicks) and a margin frame read as a gray box
    /// around the card. Edge-to-edge card = clean everywhere.
    private static func exportView() -> some View {
        WeeklyReportCard(data: .current(), rounded: false)
    }

    static func image() -> NSImage? {
        let renderer = ImageRenderer(content: exportView())
        renderer.scale = 3
        renderer.isOpaque = true
        return renderer.nsImage
    }

    static func pngData() -> Data? {
        guard let image = image(),
              let tiff = image.tiffRepresentation,
              let rep = NSBitmapImageRep(data: tiff) else { return nil }
        return rep.representation(using: .png, properties: [:])
    }

    /// Headless snapshot for tooling/screenshots:
    /// AGENTISLAND_REPORT_SNAPSHOT=/path.png renders and exits.
    static func writePNG(to path: String) {
        guard let data = pngData() else {
            NSLog("AgentIsland report: render failed")
            return
        }
        try? data.write(to: URL(fileURLWithPath: path))
    }
}

/// The window IS the card — fully borderless, because on macOS 26 a titled
/// window brings a liquid-glass slab behind the content (the "虚无缥缈的
/// 背景"). The genuine traffic-light close button is mounted manually
/// instead: NSWindow.standardWindowButton(...) gives the real AppKit
/// control (hover ✕ included), placed on the card's top-left corner.
/// Esc also closes; drag anywhere to move.
private final class ReportPanel: NSPanel {
    override var canBecomeKey: Bool { true }
    override func cancelOperation(_ sender: Any?) { close() }
}

@MainActor
final class WeeklyReportWindowController: NSWindowController, NSWindowDelegate {
    static let shared = WeeklyReportWindowController()

    private init() {
        super.init(window: nil)
    }

    @available(*, unavailable)
    required init?(coder: NSCoder) { fatalError() }

    func show() {
        if window == nil {
            let panel = ReportPanel(
                // Hugs the content exactly (card 420 + 26pt margins; buttons
                // below) — a window wider than its content reads as a ghost
                // slab around the card.
                contentRect: NSRect(origin: .zero, size: NSSize(width: 472, height: 676)),
                styleMask: [.borderless, .fullSizeContentView],
                backing: .buffered,
                defer: false
            )
            panel.backgroundColor = .clear
            panel.isOpaque = false
            panel.hasShadow = false // the card paints its own shadow
            panel.isMovableByWindowBackground = true
            // Screenshot tools (⇧⌘4/5) deactivate the app; panels hide on
            // deactivate by default, which made the card "jump away" the
            // moment the user tried to capture it.
            panel.hidesOnDeactivate = false
            panel.isReleasedWhenClosed = false
            panel.level = .floating
            panel.contentView = NSHostingView(rootView: WeeklyReportSheet())

            // The REAL red traffic light (hover ✕ and all), mounted onto the
            // card's top-left like a normal window — without the titled
            // style that drags the macOS 26 glass slab along.
            if let contentView = panel.contentView,
               let close = NSWindow.standardWindowButton(.closeButton, for: [.titled, .closable]) {
                close.target = panel
                close.action = #selector(NSWindow.close)
                contentView.addSubview(close)
                // Card sits at (26, 22) from the top-left of the content;
                // native windows inset the light ~12pt into the corner.
                // NSHostingView is FLIPPED (y grows downward) — measuring
                // from the bottom edge parked the light at the bottom.
                let inset: CGFloat = 12
                let yFromTop: CGFloat = 22 + inset
                let y = contentView.isFlipped
                    ? yFromTop
                    : contentView.bounds.height - yFromTop - close.frame.height
                close.setFrameOrigin(NSPoint(x: 26 + inset, y: y))
            }
            panel.delegate = self
            window = panel
        }
        window?.center()
        NSApp.activate(ignoringOtherApps: true)
        window?.makeKeyAndOrderFront(nil)
    }
}

private struct WeeklyReportSheet: View {
    @State private var copied = false
    @State private var toast: String?

    var body: some View {
        VStack(spacing: 14) {
            WeeklyReportCard(data: .current())
                // A tight, grounded shadow — the old radius-34/0.6 halo was
                // the "floating on fog" feel, not any system glass.
                .shadow(color: .black.opacity(0.30), radius: 10, y: 4)

            HStack(spacing: 10) {
                actionButton(copied ? L10n.tr("Copied") : L10n.tr("Copy image"), prominent: true) {
                    if copyImage() {
                        copied = true
                        DispatchQueue.main.asyncAfter(deadline: .now() + 1.6) { copied = false }
                    }
                }
                // All three buttons white — the ghost pills were invisible
                // against the dark backdrop (owner's call, 2026-07-14).
                actionButton(L10n.tr("Save PNG…"), prominent: true) { savePNG() }
                // The one native share button; WeChat/Xiaohongshu/… live
                // INSIDE the system sheet (see ShareAnchor below).
                ShareAnchor {
                    toast = L10n.tr("Image copied — paste it into the composer")
                    DispatchQueue.main.asyncAfter(deadline: .now() + 6) { toast = nil }
                }
                .frame(width: 44, height: 30)
            }

            // One-line coach mark after picking a platform.
            Text(toast ?? " ")
                .font(.system(size: 11, weight: .semibold, design: .rounded))
                .foregroundStyle(Color(red: 0.55, green: 0.85, blue: 0.62))
                .opacity(toast == nil ? 0 : 1)
                .animation(.easeOut(duration: 0.2), value: toast == nil)
        }
        .padding(.horizontal, 26)
        .padding(.top, 22)
        .padding(.bottom, 14)
    }

    @discardableResult
    private func copyImage() -> Bool {
        guard let image = WeeklyReportRenderer.image() else { return false }
        NSPasteboard.general.clearContents()
        NSPasteboard.general.writeObjects([image])
        return true
    }

    private func actionButton(_ title: String, prominent: Bool = false, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            Text(title)
                .font(.system(size: 12, weight: .bold, design: .rounded))
                .foregroundStyle(prominent ? .black : .white.opacity(0.85))
                .padding(.horizontal, 16)
                .frame(height: 30)
                .background(
                    Capsule().fill(prominent ? AnyShapeStyle(Color.white) : AnyShapeStyle(Color.white.opacity(0.12)))
                )
        }
        .buttonStyle(.plain)
    }

    private func savePNG() {
        guard let data = WeeklyReportRenderer.pngData() else { return }
        let panel = NSSavePanel()
        panel.allowedContentTypes = [.png]
        panel.nameFieldStringValue = "agent-island-weekly.png"
        if panel.runModal() == .OK, let url = panel.url {
            try? data.write(to: url)
        }
    }
}

/// The system share sheet, with our platforms INSIDE it. macOS lets an app
/// append custom NSSharingServices to the native picker via its delegate —
/// so WeChat / 公众号 / X / 小红书 / 抖音 / Instagram appear at the top of
/// the official sheet, above AirDrop and Messages, each with a brand-colored
/// tile icon. Picking one copies the PNG and opens that platform's composer
/// (none of them ship a macOS share extension, so posting = paste).
private struct ShareAnchor: NSViewRepresentable {
    /// Fired when the user picks one of OUR platforms — the sheet view shows
    /// the "image copied — paste it" coach mark.
    var onPlatformPicked: () -> Void

    func makeNSView(context: Context) -> NSButton {
        // The native macOS share control: icon-only square.and.arrow.up,
        // exactly what every system app uses — restyled as a white pill so
        // it matches the two buttons beside it (dark bezels were invisible).
        let button = NSButton(image: NSImage(systemSymbolName: "square.and.arrow.up",
                                             accessibilityDescription: L10n.tr("Share…"))!,
                              target: context.coordinator,
                              action: #selector(Coordinator.share(_:)))
        button.isBordered = false
        button.wantsLayer = true
        button.layer?.backgroundColor = NSColor.white.cgColor
        button.layer?.cornerRadius = 15
        button.contentTintColor = .black
        return button
    }

    func updateNSView(_ nsView: NSButton, context: Context) {
        context.coordinator.onPlatformPicked = onPlatformPicked
    }

    func makeCoordinator() -> Coordinator {
        let c = Coordinator()
        c.onPlatformPicked = onPlatformPicked
        return c
    }

    @MainActor
    final class Coordinator: NSObject, NSSharingServicePickerDelegate {
        var onPlatformPicked: (() -> Void)?
        // The picker dies the moment it goes out of scope — hold it while
        // the sheet is up, release in didChoose.
        private var activePicker: NSSharingServicePicker?

        @objc func share(_ sender: NSButton) {
            guard let image = WeeklyReportRenderer.image() else { return }
            let picker = NSSharingServicePicker(items: [image])
            picker.delegate = self
            activePicker = picker
            picker.show(relativeTo: sender.bounds, of: sender, preferredEdge: .minY)
        }

        // MARK: NSSharingServicePickerDelegate

        func sharingServicePicker(
            _ sharingServicePicker: NSSharingServicePicker,
            sharingServicesForItems items: [Any],
            proposedSharingServices proposedServices: [NSSharingService]
        ) -> [NSSharingService] {
            let image = items.compactMap { $0 as? NSImage }.first
            return Self.platforms.map { platform in
                NSSharingService(
                    title: L10n.tr(platform.nameKey),
                    image: Self.tileIcon(glyph: platform.glyph,
                                         top: platform.topColor,
                                         bottom: platform.bottomColor),
                    alternateImage: nil
                ) { [weak self] in
                    switch platform.action {
                    case .phoneHandoff:
                        // Mobile-first platforms (WeChat Moments, XHS, Douyin,
                        // IG): the ONLY native-feeling path on macOS is to put
                        // the image on the phone — QR over the local network,
                        // then the platform's own share UI takes over.
                        PhoneHandoffWindowController.shared.show(platformKey: platform.nameKey)
                    case .webComposer(let raw):
                        // Desktop-native platforms: copy the PNG, open the
                        // composer, paste.
                        if let image {
                            NSPasteboard.general.clearContents()
                            NSPasteboard.general.writeObjects([image])
                        }
                        if let url = URL(string: raw) { NSWorkspace.shared.open(url) }
                        self?.onPlatformPicked?()
                    }
                }
            } + proposedServices
        }

        func sharingServicePicker(_ sharingServicePicker: NSSharingServicePicker,
                                  didChoose service: NSSharingService?) {
            activePicker = nil
        }

        // MARK: Platforms

        private enum PlatformAction {
            case phoneHandoff
            case webComposer(String)
        }

        private struct Platform {
            let nameKey: String
            let glyph: String
            let topColor: NSColor
            let bottomColor: NSColor?
            let action: PlatformAction
        }

        // 公众号 removed — it's a publishing backend, not a social share
        // target (owner's call, 2026-07-14).
        private static let platforms: [Platform] = [
            Platform(nameKey: "WeChat", glyph: "微",
                     topColor: NSColor(srgbRed: 0.03, green: 0.76, blue: 0.38, alpha: 1),
                     bottomColor: nil, action: .phoneHandoff),
            Platform(nameKey: "X (Twitter)", glyph: "𝕏",
                     topColor: NSColor(srgbRed: 0.06, green: 0.08, blue: 0.10, alpha: 1),
                     bottomColor: nil,
                     action: .webComposer("https://x.com/intent/post?text=Agent%20Island%20Weekly%20%E2%80%94%20agent-island.dev")),
            Platform(nameKey: "Xiaohongshu", glyph: "红",
                     topColor: NSColor(srgbRed: 1.00, green: 0.14, blue: 0.26, alpha: 1),
                     bottomColor: nil, action: .phoneHandoff),
            Platform(nameKey: "Douyin", glyph: "抖",
                     topColor: NSColor(srgbRed: 0.09, green: 0.09, blue: 0.14, alpha: 1),
                     bottomColor: nil, action: .phoneHandoff),
            Platform(nameKey: "Instagram", glyph: "IG",
                     topColor: NSColor(srgbRed: 0.31, green: 0.36, blue: 0.84, alpha: 1),
                     bottomColor: NSColor(srgbRed: 0.84, green: 0.16, blue: 0.46, alpha: 1),
                     action: .phoneHandoff),
        ]

        /// App-Store-style rounded tile with a bold white glyph — reads as an
        /// app icon inside the share sheet. Drawn, not bundled: shipping real
        /// platform logos in the binary is a trademark headache.
        private static func tileIcon(glyph: String, top: NSColor, bottom: NSColor?) -> NSImage {
            let size = NSSize(width: 18, height: 18)
            let image = NSImage(size: size, flipped: false) { rect in
                let path = NSBezierPath(roundedRect: rect.insetBy(dx: 0.5, dy: 0.5),
                                        xRadius: 4.5, yRadius: 4.5)
                if let bottom {
                    NSGradient(starting: top, ending: bottom)?.draw(in: path, angle: -65)
                } else {
                    top.setFill()
                    path.fill()
                }
                // Dark tiles (X, Douyin) would vanish on a dark sheet.
                NSColor.white.withAlphaComponent(0.22).setStroke()
                path.lineWidth = 0.5
                path.stroke()

                let style = NSMutableParagraphStyle()
                style.alignment = .center
                let text = NSAttributedString(string: glyph, attributes: [
                    .font: NSFont.systemFont(ofSize: glyph.count > 1 ? 8 : 9.5, weight: .heavy),
                    .foregroundColor: NSColor.white,
                    .paragraphStyle: style,
                ])
                let height = text.size().height
                text.draw(in: NSRect(x: 0, y: (rect.height - height) / 2 - 0.5,
                                     width: rect.width, height: height))
                return true
            }
            // Template=false so the sheet keeps our brand colors.
            image.isTemplate = false
            return image
        }
    }
}

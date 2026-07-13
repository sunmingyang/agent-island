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

    /// "Share to <platform>" = copy the PNG to the clipboard, then open that
    /// platform's composer/upload page — paste (or drop) and post. No SDKs,
    /// no upload from us; works for every platform that has a web composer.
    private struct Platform: Identifiable {
        let id: String
        let nameKey: String
        let url: String?
        let appBundleID: String?
    }

    private static let platforms: [Platform] = [
        .init(id: "wechat", nameKey: "WeChat", url: nil, appBundleID: "com.tencent.xinWeChat"),
        .init(id: "mp", nameKey: "WeChat Official Accounts", url: "https://mp.weixin.qq.com/", appBundleID: nil),
        .init(id: "x", nameKey: "X (Twitter)", url: "https://x.com/intent/post?text=Agent%20Island%20Weekly%20%E2%80%94%20agent-island.dev", appBundleID: nil),
        .init(id: "xhs", nameKey: "Xiaohongshu", url: "https://creator.xiaohongshu.com/publish/publish", appBundleID: nil),
        .init(id: "douyin", nameKey: "Douyin", url: "https://creator.douyin.com/creator-micro/content/upload", appBundleID: nil),
        .init(id: "instagram", nameKey: "Instagram", url: "https://www.instagram.com/", appBundleID: nil),
    ]

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
                actionButton(L10n.tr("Save PNG…")) { savePNG() }
                shareToMenu
                ShareAnchor()
                    .frame(width: 40, height: 30)
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

    private var shareToMenu: some View {
        Menu {
            ForEach(Self.platforms) { p in
                Button(L10n.tr(p.nameKey)) { share(to: p) }
            }
        } label: {
            Text(L10n.tr("Share to…"))
                .font(.system(size: 12, weight: .bold, design: .rounded))
                .foregroundStyle(.white.opacity(0.85))
                .padding(.horizontal, 14)
                .frame(height: 30)
                .background(Capsule().fill(Color.white.opacity(0.12)))
        }
        .menuStyle(.borderlessButton)
        .menuIndicator(.hidden)
        .fixedSize()
    }

    @discardableResult
    private func copyImage() -> Bool {
        guard let image = WeeklyReportRenderer.image() else { return false }
        NSPasteboard.general.clearContents()
        NSPasteboard.general.writeObjects([image])
        return true
    }

    private func share(to platform: Platform) {
        guard copyImage() else { return }
        toast = L10n.tr("Image copied — paste it into the composer")
        DispatchQueue.main.asyncAfter(deadline: .now() + 6) { toast = nil }
        if let bundleID = platform.appBundleID,
           let appURL = NSWorkspace.shared.urlForApplication(withBundleIdentifier: bundleID) {
            NSWorkspace.shared.openApplication(at: appURL, configuration: .init())
        } else if let raw = platform.url, let url = URL(string: raw) {
            NSWorkspace.shared.open(url)
        }
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

/// System share sheet anchor (AirDrop, WeChat when its mac app is installed,
/// Messages, …). Douyin/Xiaohongshu/Jike have no macOS share targets — the
/// cross-platform landing hook is the QR printed on the card itself.
private struct ShareAnchor: NSViewRepresentable {
    func makeNSView(context: Context) -> NSButton {
        // The native macOS share control: icon-only square.and.arrow.up,
        // exactly what every system app uses.
        let button = NSButton(image: NSImage(systemSymbolName: "square.and.arrow.up",
                                             accessibilityDescription: L10n.tr("Share…"))!,
                              target: context.coordinator,
                              action: #selector(Coordinator.share(_:)))
        button.bezelStyle = .rounded
        button.controlSize = .regular
        return button
    }

    func updateNSView(_ nsView: NSButton, context: Context) {}

    func makeCoordinator() -> Coordinator { Coordinator() }

    @MainActor
    final class Coordinator: NSObject {
        @objc func share(_ sender: NSButton) {
            guard let image = WeeklyReportRenderer.image() else { return }
            let picker = NSSharingServicePicker(items: [image])
            picker.show(relativeTo: sender.bounds, of: sender, preferredEdge: .minY)
        }
    }
}

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
                contentRect: NSRect(origin: .zero, size: NSSize(width: 520, height: 700)),
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
                let inset: CGFloat = 12
                close.setFrameOrigin(NSPoint(
                    x: 26 + inset,
                    y: contentView.bounds.height - 22 - inset - close.frame.height
                ))
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

    var body: some View {
        VStack(spacing: 18) {
            WeeklyReportCard(data: .current())
                .shadow(color: .black.opacity(0.6), radius: 34, y: 16)

            HStack(spacing: 10) {
                actionButton(copied ? L10n.tr("Copied") : L10n.tr("Copy image"), prominent: true) {
                    if let image = WeeklyReportRenderer.image() {
                        NSPasteboard.general.clearContents()
                        NSPasteboard.general.writeObjects([image])
                        copied = true
                        DispatchQueue.main.asyncAfter(deadline: .now() + 1.6) { copied = false }
                    }
                }
                actionButton(L10n.tr("Save PNG…")) { savePNG() }
                ShareAnchor()
                    .frame(width: 92, height: 30)
            }
        }
        .padding(.horizontal, 26)
        .padding(.top, 22)
        .padding(.bottom, 24)
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

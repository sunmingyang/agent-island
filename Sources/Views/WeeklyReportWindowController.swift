import AppKit
import SwiftUI

/// Renders the weekly report card to a crisp PNG (3x) and hosts the share
/// window: the card, plus Copy / Save / system Share. Sharing is always the
/// USER posting an image — nothing leaves the machine on its own.
@MainActor
enum WeeklyReportRenderer {
    static func image() -> NSImage? {
        let renderer = ImageRenderer(content: WeeklyReportCard(data: .current()))
        renderer.scale = 3
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
            let panel = NSPanel(
                contentRect: NSRect(origin: .zero, size: NSSize(width: 480, height: 660)),
                styleMask: [.titled, .closable, .fullSizeContentView],
                backing: .buffered,
                defer: false
            )
            panel.title = L10n.tr("Weekly report")
            panel.titleVisibility = .hidden
            panel.titlebarAppearsTransparent = true
            panel.isMovableByWindowBackground = true
            panel.backgroundColor = NSColor(calibratedWhite: 0.02, alpha: 1)
            panel.isReleasedWhenClosed = false
            panel.contentView = NSHostingView(rootView: WeeklyReportSheet())
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
        VStack(spacing: 16) {
            WeeklyReportCard(data: .current())
                .shadow(color: .black.opacity(0.55), radius: 28, y: 14)

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
            .padding(.bottom, 4)
        }
        .padding(24)
        .frame(width: 480)
    }

    private func actionButton(_ title: String, prominent: Bool = false, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            Text(title)
                .font(.system(size: 12, weight: .bold, design: .rounded))
                .foregroundStyle(prominent ? .black : .white.opacity(0.85))
                .padding(.horizontal, 16)
                .frame(height: 30)
                .background(
                    Capsule().fill(prominent ? AnyShapeStyle(Color.white) : AnyShapeStyle(Color.white.opacity(0.10)))
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
/// Messages, …). Douyin/Xiaohongshu have no macOS share targets — the real
/// path there is Save/AirDrop to the phone and post from it.
private struct ShareAnchor: NSViewRepresentable {
    func makeNSView(context: Context) -> NSButton {
        let button = NSButton(title: L10n.tr("Share…"), target: context.coordinator,
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

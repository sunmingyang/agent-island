import AppKit
import SwiftUI

/// Post-update "what's new" guide (owner call, 1.7.2 planning): after an
/// update lands, tell the user what changed — Typeless-style — instead of
/// leaving them to discover features by accident. Shows once per version,
/// never inside the demo/recording rigs.
@MainActor
enum WhatsNewGate {
    private static let seenKey = "AgentIsland.whatsNewSeenVersion"

    static var currentVersion: String {
        Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "0"
    }

    static func maybeShow() {
        guard WhatsNewPref.shared.autoShow else { return }
        guard !AppEnvironment.isDemo,
              ProcessInfo.processInfo.environment["AGENTISLAND_UI_SCRIPT"] == nil,
              ProcessInfo.processInfo.environment["AGENTISLAND_REPORT_SNAPSHOT"] == nil
        else { return }
        guard UserDefaults.standard.string(forKey: seenKey) != currentVersion else { return }
        WhatsNewWindowController.shared.show()
    }

    static func markSeen() {
        UserDefaults.standard.set(currentVersion, forKey: seenKey)
    }
}

/// User control over the after-update popup (owner call: the logic must be
/// customizable). Reopening from Settings/footer/version pill always works.
@MainActor
final class WhatsNewPref: ObservableObject {
    static let shared = WhatsNewPref()

    private static let key = "AgentIsland.whatsNewAutoShow"

    @Published var autoShow: Bool {
        didSet { UserDefaults.standard.set(autoShow, forKey: Self.key) }
    }

    private init() {
        if UserDefaults.standard.object(forKey: Self.key) == nil {
            self.autoShow = true
        } else {
            self.autoShow = UserDefaults.standard.bool(forKey: Self.key)
        }
    }
}

/// The version's highlights — hand-written per release, not raw changelog.
struct WhatsNewItem: Identifiable {
    let id = UUID()
    let symbol: String
    let title: String
    let body: String

    static let current: [WhatsNewItem] = [
        WhatsNewItem(
            symbol: "scalemass",
            title: "Codex accounting v3",
            body: "The local ledger now drops fork phantom tokens — single-machine days land within about 2% of the official client"
        ),
        WhatsNewItem(
            symbol: "checkmark.seal",
            title: "Official figures, side by side",
            body: "The usage calendar shows the Codex client's own lifetime and daily numbers — account-wide, every device"
        ),
        WhatsNewItem(
            symbol: "slider.horizontal.3",
            title: "Token metric toggle works everywhere",
            body: "All tokens vs Input + output now drives the calendar and both report cards"
        ),
        WhatsNewItem(
            symbol: "rectangle.on.rectangle",
            title: "Steadier weekly card",
            body: "Every number on the card comes from one scan window, and opening the report refreshes it live"
        ),
        WhatsNewItem(
            symbol: "wand.and.stars",
            title: "Cleaner defaults",
            body: "Usage display on by default, stepped chart as the default look, sparkline retired, cost section tidied"
        ),
    ]
}

@MainActor
final class WhatsNewWindowController: NSObject, NSWindowDelegate {
    static let shared = WhatsNewWindowController()
    private var window: NSWindow?

    func show() {
        if window == nil {
            let panel = WhatsNewPanel(
                contentRect: NSRect(origin: .zero, size: NSSize(width: 430, height: 560)),
                styleMask: [.borderless, .fullSizeContentView],
                backing: .buffered,
                defer: false
            )
            panel.backgroundColor = .clear
            panel.isOpaque = false
            panel.hasShadow = false
            panel.isMovableByWindowBackground = true
            panel.hidesOnDeactivate = false
            panel.isReleasedWhenClosed = false
            panel.level = .floating
            panel.delegate = self
            window = panel
        }
        window?.contentView = NSHostingView(rootView: WhatsNewSheet())
        window?.center()
        NSApp.activate(ignoringOtherApps: true)
        window?.makeKeyAndOrderFront(nil)
    }

    func dismiss() {
        WhatsNewGate.markSeen()
        window?.close()
    }

    func windowWillClose(_ notification: Notification) {
        // Closing by any path counts as seen — never nag twice per version.
        WhatsNewGate.markSeen()
    }
}

private final class WhatsNewPanel: NSPanel {
    override var canBecomeKey: Bool { true }
    override func cancelOperation(_ sender: Any?) { close() }
}

private struct WhatsNewSheet: View {
    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            // Wordmark line, report-card family style.
            HStack(spacing: 8) {
                Text("AGENT ISLAND")
                    .font(.system(size: 13, weight: .heavy, design: .rounded))
                    .tracking(3.2)
                    .foregroundStyle(.white)
                Text(WhatsNewGate.currentVersion)
                    .font(.system(size: 11, weight: .bold, design: .rounded))
                    .monospacedDigit()
                    .foregroundStyle(IslandColor.liveTeal)
                    .padding(.horizontal, 7)
                    .padding(.vertical, 2)
                    .background(Capsule().fill(IslandColor.liveTeal.opacity(0.12)))
                Spacer()
            }
            .padding(.bottom, 14)

            Text(L10n.tr("What's new in this update"))
                .font(.system(size: 21, weight: .black, design: .rounded))
                .foregroundStyle(.white)
                .padding(.bottom, 18)

            VStack(alignment: .leading, spacing: 15) {
                ForEach(WhatsNewItem.current) { item in
                    HStack(alignment: .top, spacing: 12) {
                        Image(systemName: item.symbol)
                            .font(.system(size: 15, weight: .semibold))
                            .foregroundStyle(IslandColor.liveTeal)
                            .frame(width: 24, height: 24)
                            .background(
                                RoundedRectangle(cornerRadius: 7, style: .continuous)
                                    .fill(Color.white.opacity(0.05))
                            )
                        VStack(alignment: .leading, spacing: 3) {
                            Text(L10n.tr(item.title))
                                .font(.system(size: 13, weight: .bold, design: .rounded))
                                .foregroundStyle(.white.opacity(0.92))
                            Text(L10n.tr(item.body))
                                .font(.system(size: 11.5, weight: .medium, design: .rounded))
                                .foregroundStyle(.white.opacity(0.55))
                                .fixedSize(horizontal: false, vertical: true)
                                .lineSpacing(2)
                        }
                    }
                }
            }

            Spacer(minLength: 20)

            Button {
                WhatsNewWindowController.shared.dismiss()
            } label: {
                Text(L10n.tr("Get started"))
                    .font(.system(size: 13, weight: .heavy, design: .rounded))
                    .foregroundStyle(.black.opacity(0.9))
                    .frame(maxWidth: .infinity)
                    .frame(height: 38)
                    .background(
                        RoundedRectangle(cornerRadius: 12, style: .continuous)
                            .fill(IslandColor.liveTeal)
                    )
            }
            .buttonStyle(.plain)
        }
        .padding(26)
        .frame(width: 430)
        .background(
            RoundedRectangle(cornerRadius: 26, style: .continuous)
                .fill(WeeklyReportCard.baseCoat)
                .overlay(
                    RoundedRectangle(cornerRadius: 26, style: .continuous)
                        .strokeBorder(Color.white.opacity(0.06), lineWidth: 1)
                )
        )
        .shadow(color: .black.opacity(0.35), radius: 18, y: 8)
        .padding(20)
    }
}

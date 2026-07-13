import SwiftUI
import AppKit

@main
struct AgentIslandApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) var appDelegate
    var body: some Scene {
        MenuBarExtra {
            MenuBarStatusView()
        } label: {
            MenuBarStatusLabel()
        }
        .menuBarExtraStyle(.menu)
    }
}

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    var island: IslandWindowController?
    private var settingsShortcutMonitor: Any?
    private var backgroundActivity: NSObjectProtocol?

    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.accessory)

        // Keep monitoring while the screen is locked. As an `.accessory` /
        // LSUIElement app with no visible window, we're a prime App Nap target:
        // once the screen locks, macOS suspends our timers, so a quota window
        // that resets while you're away is never detected and auto-resume never
        // fires — exactly the "locked screen = no resume" report. A background
        // activity assertion opts out of App Nap so the refresh and trigger
        // timers keep running. `.background` only disables App Nap; it does NOT
        // set `idleSystemSleepDisabled`, so it never keeps the Mac awake — a
        // Mac that actually sleeps still can't resume, which is expected.
        backgroundActivity = ProcessInfo.processInfo.beginActivity(
            options: .background,
            reason: "Monitor quota resets to auto-resume sessions while the screen is locked"
        )
        UserDefaults.standard.removeObject(forKey: "NSWindow Frame com_apple_SwiftUI_Settings_window")
        island = IslandWindowController()
        island?.show()

        settingsShortcutMonitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { event in
            if event.modifierFlags.intersection(.deviceIndependentFlagsMask) == .command,
               event.charactersIgnoringModifiers == "," {
                SettingsWindowController.shared.show()
                return nil
            }
            return event
        }

        // Start fetching at app launch — NOT on view appear — so the panel
        // already has cached values the first time the user hovers, instead
        // of flashing "0%" while the first request lands.
        UsageStore.shared.startAutoRefresh()
        CostStore.shared.startAutoRefresh()

        // Wire the alert engine after the usage store so its initial
        // recompute sees whatever values the first refresh has produced.
        AlertEngine.shared.start()

        // Quota-exhaustion alarm rides the same usage signal: a window hitting
        // 100% pops the distinct "out of quota until <time>" alarm.
        UsageExhaustionAlarm.shared.start()

        // Auto-resume is retired (product call, 2026-07-13): the engine no
        // longer starts, so nothing is ever spawned — the page, settings tab,
        // and this start are the three gates; restore by re-enabling them.
        // TriggerEngine.shared.start()

        AgentReminderCenter.shared.start()
        ActivityMonitor.shared.start()
        showDemoTurnAlarmIfNeeded()

        // Touch the shared updater so Sparkle starts its background scheduler.
        _ = UpdaterController.shared

        // Headless card snapshot for tooling: waits for the cost scan to
        // land, renders the weekly report PNG, and exits. Mirrors the alarm
        // snapshot tooling used for release screenshots.
        if let path = ProcessInfo.processInfo.environment["AGENTISLAND_REPORT_SNAPSHOT"] {
            Task { @MainActor in
                try? await Task.sleep(nanoseconds: 5_000_000_000)
                WeeklyReportRenderer.writePNG(to: path)
                NSApp.terminate(nil)
            }
        }
        // Same idea for the reset-card chip (taste iterations need eyes):
        // renders the chip at 10x against the panel background and exits.
        if let path = ProcessInfo.processInfo.environment["AGENTISLAND_CHIP_SNAPSHOT"] {
            Task { @MainActor in
                try? await Task.sleep(nanoseconds: 1_000_000_000)
                let sample = [
                    ResetCard(id: "a", title: "Full reset",
                              expiresAt: Date().addingTimeInterval(29 * 86400)),
                    ResetCard(id: "b", title: "Full reset",
                              expiresAt: Date().addingTimeInterval(29 * 86400)),
                ]
                let view = ResetCardChip(count: 2, cards: sample)
                    .padding(24)
                    .background(Color(red: 0.02, green: 0.02, blue: 0.027))
                let renderer = ImageRenderer(content: view)
                renderer.scale = 10
                if let tiff = renderer.nsImage?.tiffRepresentation,
                   let rep = NSBitmapImageRep(data: tiff),
                   let png = rep.representation(using: .png, properties: [:]) {
                    try? png.write(to: URL(fileURLWithPath: path))
                }
                NSApp.terminate(nil)
            }
        }
    }

    func applicationWillTerminate(_ notification: Notification) {
        // Give the background activity a defined end. It is meant to be held
        // for the whole session (App Nap off while we monitor), and quitting is
        // the one moment that ends the session — so release it here rather than
        // leaning on process teardown.
        if let backgroundActivity {
            ProcessInfo.processInfo.endActivity(backgroundActivity)
            self.backgroundActivity = nil
        }
    }

    private func showDemoTurnAlarmIfNeeded() {
        guard AppEnvironment.isDemo else { return }
        // Headless marketing/release-notes renders: writes both alarm cards as
        // PNGs (in-process ImageRenderer, no screen-recording permission
        // needed) into the given directory, then quits.
        if let dir = ProcessInfo.processInfo.environment["AGENTISLAND_DEMO_ALARM_SNAPSHOT"] {
            Task { @MainActor in
                try? await Task.sleep(nanoseconds: 400_000_000)
                let thread = ActivityMonitor.ActiveThread(
                    sessionId: "00000000-0000-0000-0000-000000000000",
                    label: L10n.tr("Demo thread"),
                    cwd: NSHomeDirectory() + "/Documents/Agent Island",
                    modified: Date(),
                    transcriptPath: nil,
                    turnKey: "demo",
                    launchTarget: .cli
                )
                Self.writeAlarmSnapshot(
                    TurnAlarmView(provider: .codex, providerName: "Codex", thread: thread, kind: .yourTurn, dismiss: {}),
                    to: dir + "/turn-alarm.png"
                )
                Self.writeAlarmSnapshot(
                    TurnAlarmView(
                        provider: .claude, providerName: "Claude", thread: nil,
                        kind: .quotaExhausted(window: .fiveHour, resetAt: Date().addingTimeInterval(2 * 3600 + 7 * 60)),
                        dismiss: {}
                    ),
                    to: dir + "/quota-alarm.png"
                )
                Self.writeAlarmSnapshot(
                    TurnAlarmView(
                        provider: .codex, providerName: "Codex", thread: nil,
                        kind: .quotaExhausted(window: .fiveHour, resetAt: Date().addingTimeInterval(3 * 3600 + 22 * 60)),
                        dismiss: {}
                    ),
                    to: dir + "/quota-alarm-codex.png"
                )
                NSApp.terminate(nil)
            }
            return
        }
        guard let raw = ProcessInfo.processInfo.environment["AGENTISLAND_DEMO_TURN_ALARM"] else { return }
        let value = raw.lowercased()
        // "quota" / "quota-codex" previews the out-of-quota alarm instead of
        // the finished-turn one (screenshots and launch videos need both).
        if value.hasPrefix("quota") {
            Task { @MainActor in
                try? await Task.sleep(nanoseconds: 600_000_000)
                TurnAlarmWindowController.shared.show(
                    provider: value.contains("codex") ? .codex : .claude,
                    thread: nil,
                    kind: .quotaExhausted(window: .fiveHour, resetAt: Date().addingTimeInterval(2 * 3600 + 7 * 60))
                )
            }
            return
        }
        let provider: AlertEngine.Provider = value == "claude" ? .claude : .codex
        Task { @MainActor in
            try? await Task.sleep(nanoseconds: 600_000_000)
            let thread = ActivityMonitor.ActiveThread(
                sessionId: "00000000-0000-0000-0000-000000000000",
                label: L10n.tr("Demo thread"),
                cwd: NSHomeDirectory() + "/Documents/Agent Island",
                modified: Date(),
                transcriptPath: nil,
                turnKey: "demo",
                launchTarget: provider == .claude ? .claudeDesktop : .cli
            )
            TurnAlarmWindowController.shared.show(provider: provider, thread: thread)
        }
    }

    /// Pin the app to the run loop until the user explicitly quits.
    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool {
        false
    }

    @MainActor
    private static func writeAlarmSnapshot(_ view: TurnAlarmView, to path: String) {
        let card = view
            .frame(width: 520, height: 520)
            .clipShape(RoundedRectangle(cornerRadius: 18))
        let renderer = ImageRenderer(content: card)
        renderer.scale = 2
        guard let cg = renderer.cgImage else {
            NSLog("AgentIsland: alarm snapshot render failed for %@", path)
            return
        }
        let rep = NSBitmapImageRep(cgImage: cg)
        try? rep.representation(using: .png, properties: [:])?
            .write(to: URL(fileURLWithPath: path))
    }
}

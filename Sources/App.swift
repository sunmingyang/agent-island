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

    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.accessory)
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

        // Auto-trigger engine rides the same usage signal: a changed
        // fiveHour.resetAt means a window rolled over → resume the target
        // session. Started after the usage store for the same reason.
        TriggerEngine.shared.start()

        AgentReminderCenter.shared.start()
        ActivityMonitor.shared.start()
        showDemoTurnAlarmIfNeeded()

        // Touch the shared updater so Sparkle starts its background scheduler.
        _ = UpdaterController.shared
    }

    private func showDemoTurnAlarmIfNeeded() {
        guard AppEnvironment.isDemo,
              let raw = ProcessInfo.processInfo.environment["AGENTISLAND_DEMO_TURN_ALARM"] else { return }
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
}

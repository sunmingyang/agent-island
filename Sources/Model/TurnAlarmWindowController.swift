import AppKit
import SwiftUI

private final class TurnAlarmPanel: NSPanel {
    var onCancel: (() -> Void)?
    override var canBecomeKey: Bool { true }
    override var canBecomeMain: Bool { true }
    // An interrupting alarm must be dismissable from the keyboard; the app has
    // no menu bar, so Esc is the only chord that can reach it.
    override func cancelOperation(_ sender: Any?) { onCancel?() }
}

// The user is mid-work in another app when the alarm fires; the first click
// on a button must act, not just focus the window.
private final class TurnAlarmHostingView: NSHostingView<TurnAlarmView> {
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }
}

@MainActor
final class TurnAlarmWindowController: NSWindowController, NSWindowDelegate {
    static let shared = TurnAlarmWindowController()
    static let panelSize = NSSize(width: 520, height: 520)
    private static let expandedPanelSize = NSSize(width: 680, height: 620)
    private struct QueuedAlarm {
        let provider: AlertEngine.Provider
        let thread: ActivityMonitor.ActiveThread?
        let key: String
    }

    private var isExpanded = false
    private var alarmPanel: NSPanel?
    private var queue: [QueuedAlarm] = []
    private var current: QueuedAlarm?
    private var didAcknowledgeCurrentAlarm = false

    private init() {
        super.init(window: nil)
    }

    @available(*, unavailable)
    required init?(coder: NSCoder) { fatalError() }

    private let sound = TurnAlarmSoundLooper()

    func show(provider: AlertEngine.Provider, thread: ActivityMonitor.ActiveThread?) {
        let key = Self.alarmKey(provider: provider, thread: thread)
        guard current?.key != key, !queue.contains(where: { $0.key == key }) else { return }
        let alarm = QueuedAlarm(provider: provider, thread: thread, key: key)
        // One panel at a time: a second alarm queues instead of silently
        // replacing (and auto-acknowledging) one the user hasn't seen yet;
        // dismissing the visible panel recalls the next queued alarm.
        guard current == nil else {
            queue.append(alarm)
            return
        }
        display(alarm)
    }

    private static func alarmKey(provider: AlertEngine.Provider, thread: ActivityMonitor.ActiveThread?) -> String {
        ReminderDeliveryKey.make(
            providerRawValue: provider.rawValue,
            stateRawValue: ActivityMonitor.State.needsYou.rawValue,
            transcriptPath: thread?.transcriptPath,
            sessionId: thread?.sessionId ?? "",
            cwd: thread?.cwd ?? "",
            label: thread?.label ?? "",
            turnKey: thread?.turnKey
        )
    }

    private func display(_ alarm: QueuedAlarm) {
        let provider = alarm.provider
        let thread = alarm.thread
        let name = provider == .claude ? "Claude" : "Codex"
        current = alarm
        isExpanded = false
        didAcknowledgeCurrentAlarm = false
        let panel = TurnAlarmPanel(
            contentRect: NSRect(origin: .zero, size: Self.panelSize),
            styleMask: [.titled, .closable, .resizable, .fullSizeContentView],
            backing: .buffered,
            defer: false
        )
        let rootView = TurnAlarmView(
            provider: provider,
            providerName: name,
            thread: thread,
            dismiss: { [weak self, weak panel] in
                self?.dismissCurrentAlarm(panel)
            }
        )
        panel.contentView = TurnAlarmHostingView(rootView: rootView)
        panel.onCancel = { [weak self, weak panel] in
            self?.dismissCurrentAlarm(panel)
        }
        panel.setFrame(NSRect(origin: .zero, size: Self.panelSize), display: false)
        panel.title = L10n.tr("Turn alarm")
        panel.titleVisibility = .hidden
        panel.titlebarAppearsTransparent = true
        panel.isMovableByWindowBackground = true
        panel.minSize = Self.panelSize
        panel.maxSize = Self.panelSize
        panel.contentMinSize = Self.panelSize
        panel.contentMaxSize = Self.panelSize
        panel.isRestorable = false
        panel.isReleasedWhenClosed = false
        panel.hidesOnDeactivate = false
        panel.backgroundColor = NSColor(calibratedRed: 0.020, green: 0.020, blue: 0.027, alpha: 1)
        panel.isOpaque = true
        panel.hasShadow = true
        panel.ignoresMouseEvents = false
        panel.acceptsMouseMovedEvents = true
        panel.level = .screenSaver
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .transient, .ignoresCycle]
        panel.delegate = self
        alarmPanel = panel
        window = panel
        NSApp.activate(ignoringOtherApps: true)
        center(panel)
        panel.makeKeyAndOrderFront(nil)
        panel.orderFrontRegardless()
        sound.start()
    }

    /// Close the alarm when its provider leaves needsYou — the user already
    /// replied in the thread, so the wait this panel announced is over.
    /// Closing routes through windowWillClose; the acknowledge there is
    /// harmless (the key is already delivered and the turn is done).
    func autoDismiss(provider: AlertEngine.Provider, deliveryKey: String) {
        queue.removeAll { $0.key == deliveryKey }
        guard let current, current.key == deliveryKey, let alarmPanel else { return }
        dismissCurrentAlarm(alarmPanel)
    }

    private func dismissCurrentAlarm(_ panel: NSPanel?) {
        acknowledgeCurrentAlarm()
        sound.stop()
        panel?.orderOut(nil)
        panel?.close()
    }

    private func acknowledgeCurrentAlarm() {
        guard !didAcknowledgeCurrentAlarm, let current else { return }
        didAcknowledgeCurrentAlarm = true
        AgentReminderCenter.shared.acknowledge(provider: current.provider, thread: current.thread)
    }

    private func center(_ panel: NSPanel) {
        let screen = NSScreen.main ?? NSScreen.screens.first
        let frame = screen?.visibleFrame ?? NSRect(origin: .zero, size: NSScreen.main?.frame.size ?? Self.panelSize)
        let size = panel.frame.size
        let targetFrame = NSRect(
            origin: NSPoint(
                x: frame.midX - size.width / 2,
                y: frame.midY - size.height / 2
            ),
            size: size
        )
        panel.setFrame(targetFrame, display: true)
    }

    private func toggleZoom(_ panel: NSPanel?) {
        guard let panel else { return }
        isExpanded.toggle()
        let target = isExpanded ? Self.expandedPanelSize : Self.panelSize
        var frame = panel.frame
        let center = NSPoint(x: frame.midX, y: frame.midY)
        frame.size = target
        frame.origin = NSPoint(x: center.x - frame.width / 2, y: center.y - frame.height / 2)
        panel.minSize = target
        panel.maxSize = target
        panel.contentMinSize = target
        panel.contentMaxSize = target
        panel.setFrame(frame, display: true, animate: true)
    }

    func windowShouldClose(_ sender: NSWindow) -> Bool {
        acknowledgeCurrentAlarm()
        return true
    }

    func windowShouldZoom(_ sender: NSWindow, toFrame newFrame: NSRect) -> Bool {
        toggleZoom(sender as? NSPanel)
        return false
    }

    func windowWillClose(_ notification: Notification) {
        if notification.object as AnyObject? === alarmPanel {
            acknowledgeCurrentAlarm()
            sound.stop()
            alarmPanel = nil
            window = nil
            current = nil
            didAcknowledgeCurrentAlarm = false
            guard !queue.isEmpty else { return }
            let next = queue.removeFirst()
            // Let the close finish unwinding before building the next panel.
            DispatchQueue.main.async { [weak self] in
                guard let self else { return }
                if self.current == nil {
                    self.display(next)
                } else {
                    self.queue.insert(next, at: 0)
                }
            }
        }
    }
}

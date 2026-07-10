import Combine
import Foundation
import UserNotifications

/// Fires a distinct full-screen alarm (and a system notification) the moment a
/// provider's 5-hour or weekly window hits 100% — the "you're out of quota
/// until <time>" popup, separate from the thread-finished "it's your turn"
/// alarm. Lets the alarm mean something actionable instead of conflating a
/// finished turn with a hard rate-limit block.
///
/// Mirrors `AlertEngine`'s crossing pattern: it warms up on the first real
/// usage sample (so launching into an already-exhausted window doesn't alarm),
/// dedups per (provider, window, resetAt) so it fires once per reset cycle, and
/// prunes a key only when that window's reset boundary advances — so a percent
/// that jitters just under/over 100% within one cycle can't re-alarm.
@MainActor
final class UsageExhaustionAlarm {
    static let shared = UsageExhaustionAlarm()

    private var firedKeys: Set<String> = []
    private var warmedUp = false
    private var subs: Set<AnyCancellable> = []

    private init() {}

    func start() {
        let triggers: [AnyPublisher<Void, Never>] = [
            UsageStore.shared.$claude.map { _ in () }.eraseToAnyPublisher(),
            UsageStore.shared.$codex.map { _ in () }.eraseToAnyPublisher(),
        ]
        Publishers.MergeMany(triggers)
            .receive(on: DispatchQueue.main)
            .sink { [weak self] in Task { @MainActor in self?.recompute() } }
            .store(in: &subs)
        recompute()
    }

    private struct WindowRef {
        let provider: AlertEngine.Provider
        let window: QuotaWindowKind
        let usage: WindowUsage
    }

    private func currentWindows() -> [WindowRef] {
        let usage = UsageStore.shared
        let all = [
            WindowRef(provider: .claude, window: .fiveHour, usage: usage.claude.fiveHour),
            WindowRef(provider: .claude, window: .weekly, usage: usage.claude.weekly),
            WindowRef(provider: .codex, window: .fiveHour, usage: usage.codex.fiveHour),
            WindowRef(provider: .codex, window: .weekly, usage: usage.codex.weekly),
        ]
        // Providers switched off in Settings never alarm — same contract as
        // the island's red attention glow.
        return all.filter { ProviderVisibilityStore.shared.effectiveVisible(provider: $0.provider) }
    }

    private func key(_ provider: AlertEngine.Provider, _ window: QuotaWindowKind, _ resetAt: Date) -> String {
        "\(provider.rawValue)-\(window.rawValue)-\(Int(resetAt.timeIntervalSince1970))"
    }

    private func isExhausted(_ ref: WindowRef) -> Bool {
        ref.usage.error == nil && ref.usage.usedPercent >= 0.999
    }

    private func recompute() {
        // Never act in demo/preview — synthetic usage jumps around every tick.
        guard AppEnvironment.current == .normal else { return }
        // Only once real data has flowed (matches AlertEngine's gate), so a
        // cached/zeroed launch snapshot can't fire anything.
        guard UsageStore.shared.lastUpdated != nil else { return }

        let windows = currentWindows()

        // Prune fired keys whose window has advanced to a new reset cycle. Only
        // prune when the current resetAt is known — an error/nil boundary leaves
        // prior keys intact rather than re-arming on a transient blip.
        for ref in windows {
            guard let reset = ref.usage.resetAt else { continue }
            let prefix = "\(ref.provider.rawValue)-\(ref.window.rawValue)-"
            let currentKey = key(ref.provider, ref.window, reset)
            firedKeys = firedKeys.filter { !$0.hasPrefix(prefix) || $0 == currentKey }
        }

        // Warmup: on the first real sample, mark anything already exhausted as
        // already-alarmed so we don't pop for a state that predates launch.
        if !warmedUp {
            for ref in windows where isExhausted(ref) {
                if let reset = ref.usage.resetAt { firedKeys.insert(key(ref.provider, ref.window, reset)) }
            }
            warmedUp = true
            return
        }

        // Respect the master alarm switch — if the user turned off turn alarms,
        // don't surprise them with a quota alarm either.
        guard AgentReminderStore.shared.enabled else { return }

        for ref in windows where isExhausted(ref) {
            guard let reset = ref.usage.resetAt else { continue }
            let alarmKey = key(ref.provider, ref.window, reset)
            guard !firedKeys.contains(alarmKey) else { continue }
            firedKeys.insert(alarmKey)
            fire(ref, resetAt: reset)
        }
    }

    private func fire(_ ref: WindowRef, resetAt: Date) {
        TurnAlarmWindowController.shared.show(
            provider: ref.provider,
            thread: nil,
            kind: .quotaExhausted(window: ref.window, resetAt: resetAt)
        )
        postNotification(ref, resetAt: resetAt)
    }

    private func postNotification(_ ref: WindowRef, resetAt: Date) {
        let name = ref.provider == .claude ? "Claude" : "Codex"
        let windowName = ref.window == .fiveHour ? L10n.tr("5-hour limit") : L10n.tr("Weekly limit")
        let formatter = DateFormatter()
        formatter.locale = L10n.locale
        formatter.timeStyle = .short
        formatter.dateStyle = .none

        let content = UNMutableNotificationContent()
        content.title = L10n.tr("%@ %@ reached", name, windowName)
        content.body = L10n.tr("You're out until it resets at %@.", formatter.string(from: resetAt))

        let request = UNNotificationRequest(
            identifier: "agent-island-exhausted-\(key(ref.provider, ref.window, resetAt))",
            content: content,
            trigger: nil
        )
        UNUserNotificationCenter.current().add(request, withCompletionHandler: nil)
    }
}

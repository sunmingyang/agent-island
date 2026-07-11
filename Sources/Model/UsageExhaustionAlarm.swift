import Combine
import Foundation

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
        // Dedicated opt-out: some people only want auto-resume.
        guard QuotaAlarmStore.shared.enabled else { return }

        // One alarm per provider per pass. Claude exposes both a 5-hour and a
        // weekly window; when both cross 100% in the same refresh they used to
        // fire two separate full-screen panels (they queue back-to-back, so it
        // reads as "it keeps popping"). Collapse to a single alarm for the
        // binding window — the one with the latest reset, i.e. the time you're
        // actually blocked until — while marking every exhausted window fired
        // so neither re-arms. Windows that exhaust in *different* passes still
        // each get their own alarm.
        let exhausted = windows.filter { isExhausted($0) && $0.usage.resetAt != nil }
        for provider in Set(exhausted.map(\.provider)) {
            let group = exhausted.filter { $0.provider == provider }
            let hasUnfired = group.contains { !firedKeys.contains(key($0.provider, $0.window, $0.usage.resetAt!)) }
            guard hasUnfired else { continue }
            for ref in group { firedKeys.insert(key(ref.provider, ref.window, ref.usage.resetAt!)) }
            if let binding = group.max(by: { ($0.usage.resetAt ?? .distantPast) < ($1.usage.resetAt ?? .distantPast) }),
               let reset = binding.usage.resetAt {
                fire(binding, resetAt: reset)
            }
        }
    }

    private func fire(_ ref: WindowRef, resetAt: Date) {
        // The full-screen panel IS the notification — it's screen-saver level
        // and joins all Spaces, so it surfaces over fullscreen work on any
        // display. Posting a Notification Center banner alongside it just
        // showed the same thing twice (half of the "2-3 popups" report).
        TurnAlarmWindowController.shared.show(
            provider: ref.provider,
            thread: nil,
            kind: .quotaExhausted(window: ref.window, resetAt: resetAt)
        )
    }

}

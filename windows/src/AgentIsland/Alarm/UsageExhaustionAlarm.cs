using System.ComponentModel;
using AgentIsland.Core;
using AgentIsland.Usage;

namespace AgentIsland.Alarm;

/// Fires a distinct full-screen alarm the moment a provider's 5-hour or
/// weekly window hits 100% — the "you're out of quota until <time>" popup,
/// separate from the thread-finished "it's your turn" alarm. Lets the alarm
/// mean something actionable instead of conflating a finished turn with a
/// hard rate-limit block.
///
/// Port of the macOS UsageExhaustionAlarm: it warms up on the first real
/// usage sample (so launching into an already-exhausted window doesn't
/// alarm), dedups per (provider, window, resetAt) so it fires once per reset
/// cycle, and prunes a key only when that window's reset boundary advances —
/// so a percent that jitters just under/over 100% within one cycle can't
/// re-alarm.
///
/// Beyond the macOS port, alarms coalesce per provider: the 5-hour and
/// weekly windows usually cross 100% together, and users read the resulting
/// back-to-back popups as the same alarm firing twice. One blocked stretch
/// now means one alarm — it carries the LATEST reset among the exhausted
/// windows (the true unblock time), and any window that exhausts while that
/// reset is still in the future is consumed silently.
public sealed class UsageExhaustionAlarm
{
    public static UsageExhaustionAlarm Shared { get; } = new(
        (provider, window, resetAt) => TurnAlarmWindowController.Shared.Show(
            provider,
            null,
            QuotaAlarmKey(provider, window, resetAt),
            new TurnAlarmKind.QuotaExhausted(window, resetAt)));

    private readonly Action<TriggerTool, QuotaWindowKind, DateTimeOffset> _fire;
    private readonly HashSet<string> _firedKeys = new(StringComparer.Ordinal);
    private readonly Dictionary<TriggerTool, DateTimeOffset> _blockedUntil = new();
    private bool _warmedUp;

    internal UsageExhaustionAlarm(Action<TriggerTool, QuotaWindowKind, DateTimeOffset> fire)
    {
        _fire = fire;
    }

    public void Start()
    {
        UsageStore.Shared.PropertyChanged += OnUsageChanged;
        Recompute();
    }

    private void OnUsageChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(UsageStore.Claude) or nameof(UsageStore.Codex))
        {
            Recompute();
        }
    }

    private void Recompute()
    {
        // Never act in demo/preview — synthetic usage jumps around every tick.
        if (AppEnvironment.Current != AppMode.Normal) return;
        // Only once real data has flowed (matches AlertEngine's gate), so a
        // cached/zeroed launch snapshot can't fire anything.
        if (UsageStore.Shared.LastUpdated is null) return;
        Recompute(
            UsageStore.Shared.Claude,
            UsageStore.Shared.Codex,
            AgentReminderStore.Shared.Enabled,
            Model.ProviderVisibilityStore.Shared.ClaudeVisible,
            Model.ProviderVisibilityStore.Shared.CodexVisible);
    }

    /// Testable core — the macOS recompute() body with the store reads
    /// lifted out.
    internal void Recompute(
        AppUsage claude,
        AppUsage codex,
        bool remindersEnabled,
        bool claudeVisible = true,
        bool codexVisible = true,
        DateTimeOffset? now = null)
    {
        var clock = now ?? DateTimeOffset.Now;
        // Providers switched off in Settings never alarm - same contract
        // as the island's red attention glow.
        var all = new (TriggerTool Provider, QuotaWindowKind Window, WindowUsage Usage)[]
        {
            (TriggerTool.Claude, QuotaWindowKind.FiveHour, claude.FiveHour),
            (TriggerTool.Claude, QuotaWindowKind.Weekly, claude.Weekly),
            (TriggerTool.Codex, QuotaWindowKind.FiveHour, codex.FiveHour),
            (TriggerTool.Codex, QuotaWindowKind.Weekly, codex.Weekly),
        };
        var windows = System.Array.FindAll(all, w =>
            w.Provider == TriggerTool.Claude ? claudeVisible : codexVisible);

        // Prune fired keys whose window has advanced to a new reset cycle.
        // Only prune when the current resetAt is known — an error/nil boundary
        // leaves prior keys intact rather than re-arming on a transient blip.
        foreach (var (provider, window, usage) in windows)
        {
            if (usage.ResetAt is not { } reset) continue;
            var prefix = $"{provider.RawValue()}-{TurnAlarmKind.RawValue(window)}-";
            var currentKey = Key(provider, window, reset);
            _firedKeys.RemoveWhere(key =>
                key.StartsWith(prefix, StringComparison.Ordinal) && key != currentKey);
        }

        // Warmup: on the first real sample, mark anything already exhausted as
        // already-alarmed so we don't pop for a state that predates launch.
        if (!_warmedUp)
        {
            foreach (var (provider, window, usage) in windows)
            {
                if (!IsExhausted(usage)) continue;
                if (usage.ResetAt is not { } reset) continue;
                _firedKeys.Add(Key(provider, window, reset));
                RaiseBlockedUntil(provider, reset);
            }
            _warmedUp = true;
            return;
        }

        // Respect the master alarm switch — if the user turned off turn
        // alarms, don't surprise them with a quota alarm either.
        if (!remindersEnabled) return;

        foreach (var provider in new[] { TriggerTool.Claude, TriggerTool.Codex })
        {
            // Every newly-exhausted window of this provider, unconsumed keys
            // only. All of them get consumed by this pass either way — the
            // choice is whether the pass pops one alarm or stays silent.
            var fresh = windows
                .Where(w => w.Provider == provider
                    && IsExhausted(w.Usage)
                    && w.Usage.ResetAt is { } reset
                    && !_firedKeys.Contains(Key(provider, w.Window, reset)))
                .ToList();
            if (fresh.Count == 0) continue;

            var target = fresh.MaxBy(w => w.Usage.ResetAt!.Value);
            foreach (var (_, window, usage) in fresh)
            {
                _firedKeys.Add(Key(provider, window, usage.ResetAt!.Value));
            }

            // Still inside a stretch we already alarmed for: extend the
            // stretch to the new (later) reset, but stay silent — the user
            // was told they're blocked; a longer block is not a new event.
            var alreadyBlocked = _blockedUntil.TryGetValue(provider, out var until) && until > clock;
            RaiseBlockedUntil(provider, target.Usage.ResetAt!.Value);
            if (alreadyBlocked) continue;

            _fire(provider, target.Window, target.Usage.ResetAt!.Value);
        }
    }

    private void RaiseBlockedUntil(TriggerTool provider, DateTimeOffset reset)
    {
        if (!_blockedUntil.TryGetValue(provider, out var existing) || reset > existing)
        {
            _blockedUntil[provider] = reset;
        }
    }

    internal static bool IsExhausted(WindowUsage usage) =>
        usage.Error is null && usage.UsedPercent >= 0.999;

    private static string Key(TriggerTool provider, QuotaWindowKind window, DateTimeOffset resetAt) =>
        $"{provider.RawValue()}-{TurnAlarmKind.RawValue(window)}-{resetAt.ToUnixTimeSeconds()}";

    /// Keyed on the reset boundary so it fires once per window cycle and
    /// dedups against the currently-showing/queued exhaustion alarm — the
    /// macOS alarmKey shape.
    internal static string QuotaAlarmKey(TriggerTool provider, QuotaWindowKind window, DateTimeOffset? resetAt)
    {
        var stamp = resetAt is { } reset ? reset.ToUnixTimeSeconds().ToString() : "none";
        return $"exhausted-{provider.RawValue()}-{TurnAlarmKind.RawValue(window)}-{stamp}";
    }
}

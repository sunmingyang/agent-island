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
/// Direct port of the macOS UsageExhaustionAlarm: it warms up on the first
/// real usage sample (so launching into an already-exhausted window doesn't
/// alarm), dedups per (provider, window, resetAt) so it fires once per reset
/// cycle, and prunes a key only when that window's reset boundary advances —
/// so a percent that jitters just under/over 100% within one cycle can't
/// re-alarm.
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
        Recompute(UsageStore.Shared.Claude, UsageStore.Shared.Codex, AgentReminderStore.Shared.Enabled);
    }

    /// Testable core — the macOS recompute() body with the store reads
    /// lifted out.
    internal void Recompute(AppUsage claude, AppUsage codex, bool remindersEnabled)
    {
        var windows = new (TriggerTool Provider, QuotaWindowKind Window, WindowUsage Usage)[]
        {
            (TriggerTool.Claude, QuotaWindowKind.FiveHour, claude.FiveHour),
            (TriggerTool.Claude, QuotaWindowKind.Weekly, claude.Weekly),
            (TriggerTool.Codex, QuotaWindowKind.FiveHour, codex.FiveHour),
            (TriggerTool.Codex, QuotaWindowKind.Weekly, codex.Weekly),
        };

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
                if (usage.ResetAt is { } reset) _firedKeys.Add(Key(provider, window, reset));
            }
            _warmedUp = true;
            return;
        }

        // Respect the master alarm switch — if the user turned off turn
        // alarms, don't surprise them with a quota alarm either.
        if (!remindersEnabled) return;

        foreach (var (provider, window, usage) in windows)
        {
            if (!IsExhausted(usage)) continue;
            if (usage.ResetAt is not { } reset) continue;
            if (!_firedKeys.Add(Key(provider, window, reset))) continue;
            _fire(provider, window, reset);
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

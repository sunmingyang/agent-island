using AgentIsland.Core;

namespace AgentIsland.Trigger;

/// When a trigger fires. `AfterReset` rides the provider's real
/// 5-hour-window reset (detected by UsageStore), so it fires at the actual
/// reset instant rather than on a fixed clock. `EveryHours` is a plain
/// fixed interval.
public enum TriggerMode
{
    AfterReset,
    EveryHours,
}

/// One auto-trigger: resume SessionId in Tool with Message when Mode is
/// satisfied. Persisted as JSON via Preferences.
public sealed class Trigger
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public TriggerTool Tool { get; set; }
    public string SessionId { get; set; } = "";
    public string Label { get; set; } = "";
    public string Cwd { get; set; } = "";
    public string Message { get; set; } = Localization.L10n.Tr("Continue");
    public TriggerMode Mode { get; set; } = TriggerMode.AfterReset;
    public int EveryHours { get; set; } = 5;
    public bool Enabled { get; set; } = true;
    public DateTimeOffset? LastFired { get; set; }
}

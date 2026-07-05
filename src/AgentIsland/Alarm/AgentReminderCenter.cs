using AgentIsland.Core;

namespace AgentIsland.Alarm;

/// Turn-alarm delivery pipeline. The full implementation (delivery keys,
/// storm collapse, confirmation buffer, queueing, auto-dismiss) lands with
/// the alarm milestone; the activity monitor already routes needs-you threads
/// through this surface so the wiring shape matches macOS from day one.
public sealed partial class AgentReminderCenter
{
    public static AgentReminderCenter Shared { get; } = new();

    /// True when the user has already acknowledged this thread's current
    /// finished turn — an acknowledged turn must not pin the logo.
    public bool HasAcknowledged(TriggerTool provider, ActivityMonitor.ActiveThread thread) =>
        HasAcknowledgedCore(provider, thread);

    /// Every needs-you thread for the provider, newest first.
    public void Handle(TriggerTool provider, IReadOnlyList<ActivityMonitor.ActiveThread> needsYouThreads) =>
        HandleCore(provider, needsYouThreads);

    private bool HasAcknowledgedCore(TriggerTool provider, ActivityMonitor.ActiveThread thread) => false;

    private void HandleCore(TriggerTool provider, IReadOnlyList<ActivityMonitor.ActiveThread> needsYouThreads)
    {
    }
}

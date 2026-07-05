using System.ComponentModel;
using AgentIsland.Core;

namespace AgentIsland.Trigger;

/// User-configured auto-triggers. Default empty: the feature does nothing
/// until the user adds a trigger, so fresh installs see no behavior change.
public sealed class TriggerStore : INotifyPropertyChanged
{
    public static TriggerStore Shared { get; } = new();

    private const string Key = "AgentIsland.triggers";
    private List<Trigger> _triggers;

    public event PropertyChangedEventHandler? PropertyChanged;

    private TriggerStore()
    {
        _triggers = Preferences.Get<List<Trigger>?>(Key) ?? new List<Trigger>();
    }

    public IReadOnlyList<Trigger> Triggers => _triggers;

    public void Add(Trigger trigger)
    {
        _triggers.Add(trigger);
        Persist();
    }

    public void Remove(string id)
    {
        _triggers.RemoveAll(trigger => trigger.Id == id);
        Persist();
    }

    public void Update(Trigger trigger)
    {
        var index = _triggers.FindIndex(existing => existing.Id == trigger.Id);
        if (index < 0) return;
        _triggers[index] = trigger;
        Persist();
    }

    public void SetEnabled(string id, bool enabled)
    {
        var trigger = _triggers.FirstOrDefault(existing => existing.Id == id);
        if (trigger is null) return;
        trigger.Enabled = enabled;
        Persist();
    }

    public void MarkFired(string id, DateTimeOffset? at = null)
    {
        var trigger = _triggers.FirstOrDefault(existing => existing.Id == id);
        if (trigger is null) return;
        trigger.LastFired = at ?? DateTimeOffset.Now;
        Persist();
    }

    private void Persist()
    {
        Preferences.Set(Key, _triggers);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Triggers)));
    }
}

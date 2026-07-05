using System.ComponentModel;

namespace AgentIsland.Usage;

/// Live provider usage published to the UI and the activity monitor. The
/// fetch/caching pipeline lands with the usage milestone; the observable
/// surface is defined now so downstream consumers wire against the final
/// shape.
public sealed class UsageStore : INotifyPropertyChanged
{
    public static UsageStore Shared { get; } = new();

    private AppUsage _claude = AppUsage.Empty;
    private AppUsage _codex = AppUsage.Empty;
    private DateTimeOffset? _lastUpdated;
    private bool _loading;

    public event PropertyChangedEventHandler? PropertyChanged;

    public AppUsage Claude
    {
        get => _claude;
        internal set { _claude = value; Raise(nameof(Claude)); }
    }

    public AppUsage Codex
    {
        get => _codex;
        internal set { _codex = value; Raise(nameof(Codex)); }
    }

    public DateTimeOffset? LastUpdated
    {
        get => _lastUpdated;
        internal set { _lastUpdated = value; Raise(nameof(LastUpdated)); }
    }

    public bool Loading
    {
        get => _loading;
        internal set { _loading = value; Raise(nameof(Loading)); }
    }

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

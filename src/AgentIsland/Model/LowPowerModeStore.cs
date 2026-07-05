using System.ComponentModel;
using AgentIsland.Core;

namespace AgentIsland.Model;

/// Low Power Mode: the steady-state glow effects rest, pulsing only on
/// refresh, hover, or limit alerts. Alert tints override it — an explicit
/// safety preference beats an idle-aesthetic one.
public sealed class LowPowerModeStore : INotifyPropertyChanged
{
    private const string Key = "MacIsland.lowPowerMode";

    public static LowPowerModeStore Shared { get; } = new();

    private bool _enabled;

    public event PropertyChangedEventHandler? PropertyChanged;

    private LowPowerModeStore()
    {
        _enabled = Preferences.Get<bool?>(Key) ?? false;
    }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            Preferences.Set(Key, value);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Enabled)));
        }
    }
}

using System.ComponentModel;
using AgentIsland.Core;

namespace AgentIsland.Model;

/// Approaching-limit alert thresholds. Opt-in (default off) so existing
/// installs never get a surprise visual change; warning stays below
/// critical by clamping.
public sealed class AlertThresholdStore : INotifyPropertyChanged
{
    public static AlertThresholdStore Shared { get; } = new();

    private bool _enabled;
    private int _warningPercent;
    private int _criticalPercent;

    public event PropertyChangedEventHandler? PropertyChanged;

    private AlertThresholdStore()
    {
        _enabled = Preferences.Get<bool?>("MacIsland.alertsEnabled") ?? false;
        _warningPercent = Math.Clamp(Preferences.Get<int?>("MacIsland.alertWarning") ?? 80, 50, 98);
        _criticalPercent = Math.Clamp(Preferences.Get<int?>("MacIsland.alertCritical") ?? 95, 51, 99);
    }

    public bool Enabled
    {
        get => _enabled;
        set { _enabled = value; Preferences.Set("MacIsland.alertsEnabled", value); Raise(nameof(Enabled)); }
    }

    public int WarningPercent
    {
        get => _warningPercent;
        set
        {
            _warningPercent = Math.Clamp(value, 50, Math.Min(98, _criticalPercent - 1));
            Preferences.Set("MacIsland.alertWarning", _warningPercent);
            Raise(nameof(WarningPercent));
        }
    }

    public int CriticalPercent
    {
        get => _criticalPercent;
        set
        {
            _criticalPercent = Math.Clamp(value, Math.Max(51, _warningPercent + 1), 99);
            Preferences.Set("MacIsland.alertCritical", _criticalPercent);
            Raise(nameof(CriticalPercent));
        }
    }

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

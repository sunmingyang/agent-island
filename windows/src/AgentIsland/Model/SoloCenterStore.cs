using System.ComponentModel;
using AgentIsland.Core;

namespace AgentIsland.Model;

/// Whether the island pulls its content to the middle when exactly one
/// provider is visible. Windows-only: macOS sits over a physical notch, so
/// its layout must stay symmetric around the camera housing — here the
/// center gap is just pixels, and a lone provider parked off to one side
/// reads as a bug.
public sealed class SoloCenterStore : INotifyPropertyChanged
{
    public static SoloCenterStore Shared { get; } = new();

    private const string EnabledKey = "AgentIsland.centerWhenSolo";

    private bool _enabled;

    public event PropertyChangedEventHandler? PropertyChanged;

    private SoloCenterStore()
    {
        _enabled = Preferences.Get<bool?>(EnabledKey) ?? true;
    }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            _enabled = value;
            Preferences.Set(EnabledKey, value);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Enabled)));
        }
    }
}

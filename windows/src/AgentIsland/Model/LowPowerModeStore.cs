using System.ComponentModel;
using System.Runtime.InteropServices;
using AgentIsland.Core;

namespace AgentIsland.Model;

/// Visual effects: Calm (true) rests the steady-state glow — halo and orbit
/// light only on hover, refresh, or alerts — while Vivid (false) keeps them
/// always on. The class keeps its historical name because the persisted key
/// does: existing users' explicit choice must survive the rename to
/// "Visual effects" (their old Low Power ON reads as Calm, OFF as Vivid).
/// Alert tints override Calm — a safety signal beats an idle aesthetic.
public sealed class LowPowerModeStore : INotifyPropertyChanged
{
    private const string Key = "MacIsland.lowPowerMode";

    public static LowPowerModeStore Shared { get; } = new();

    private bool _enabled;
    private bool _systemLowPower;

    public event PropertyChangedEventHandler? PropertyChanged;

    private LowPowerModeStore()
    {
        // Calm unless the user explicitly chose Vivid: a missing key means
        // "never touched" and must land on Calm, so read presence (null)
        // rather than a bool default.
        _enabled = Preferences.Get<bool?>(Key) ?? true;
        _systemLowPower = ReadSystemLowPower();
        Microsoft.Win32.SystemEvents.PowerModeChanged += (_, _) =>
        {
            var now = ReadSystemLowPower();
            if (now == _systemLowPower) return;
            _systemLowPower = now;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EffectiveEnabled)));
        };
    }

    /// The user's picker choice: true = Calm, false = Vivid.
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            Preferences.Set(Key, value);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Enabled)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EffectiveEnabled)));
        }
    }

    /// What render gating should read: the system battery saver forces Calm
    /// without touching the stored choice, so Vivid returns when it lifts —
    /// the same convention macOS Low Power Mode follows.
    public bool EffectiveEnabled => _enabled || _systemLowPower;

    // Windows battery saver ("energy saver"): SYSTEM_POWER_STATUS's
    // SystemStatusFlag is 1 while it's on.
    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);

    private static bool ReadSystemLowPower()
    {
        try
        {
            return GetSystemPowerStatus(out var status) && status.SystemStatusFlag == 1;
        }
        catch
        {
            return false;
        }
    }
}

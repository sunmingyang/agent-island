using System.ComponentModel;
using System.IO;
using AgentIsland.Core;

namespace AgentIsland.Alarm;

/// Turn-alarm preferences: master switch, sound on/off, volume, sound
/// choice (Windows Media presets or a custom file), and whether the alarm
/// window shows session details.
public sealed class AgentReminderStore : INotifyPropertyChanged
{
    public static AgentReminderStore Shared { get; } = new();

    private const string EnabledKey = "AgentIsland.agentReminders";
    private const string SoundEnabledKey = "AgentIsland.agentReminderSound";
    private const string VolumeKey = "AgentIsland.agentReminderVolume";
    private const string SoundChoiceKey = "AgentIsland.agentReminderSoundChoice";
    private const string CustomSoundKey = "AgentIsland.agentReminderCustomSound";
    private const string ShowDetailsKey = "AgentIsland.agentReminderShowSessionDetails";

    /// Built-in presets resolved against C:\Windows\Media — the Windows
    /// counterpart of the macOS system-sound list.
    public static readonly string[] SoundPresets =
    {
        "Alarm01", "Alarm02", "Alarm05", "Alarm10",
        "Ring01", "Ring05", "chimes", "chord", "notify", "tada",
    };

    public const string CustomSoundChoice = "Custom";

    private bool _enabled;
    private bool _soundEnabled;
    private double _volume;
    private string _soundChoice;
    private string _customSoundPath;
    private bool _showSessionDetails;

    public event PropertyChangedEventHandler? PropertyChanged;

    private AgentReminderStore()
    {
        _enabled = Preferences.Get<bool?>(EnabledKey) ?? true;
        _soundEnabled = Preferences.Get<bool?>(SoundEnabledKey) ?? true;
        _volume = Math.Clamp(Preferences.Get<double?>(VolumeKey) ?? 0.8, 0, 1);
        _soundChoice = Preferences.Get<string?>(SoundChoiceKey) ?? "Alarm01";
        _customSoundPath = Preferences.Get<string?>(CustomSoundKey) ?? "";
        _showSessionDetails = Preferences.Get<bool?>(ShowDetailsKey) ?? false;
    }

    public bool Enabled
    {
        get => _enabled;
        set { _enabled = value; Preferences.Set(EnabledKey, value); Raise(nameof(Enabled)); }
    }

    public bool SoundEnabled
    {
        get => _soundEnabled;
        set { _soundEnabled = value; Preferences.Set(SoundEnabledKey, value); Raise(nameof(SoundEnabled)); }
    }

    public double Volume
    {
        get => _volume;
        set { _volume = Math.Clamp(value, 0, 1); Preferences.Set(VolumeKey, _volume); Raise(nameof(Volume)); }
    }

    public string SoundChoice
    {
        get => _soundChoice;
        set { _soundChoice = value; Preferences.Set(SoundChoiceKey, value); Raise(nameof(SoundChoice)); }
    }

    public string CustomSoundPath
    {
        get => _customSoundPath;
        set { _customSoundPath = value; Preferences.Set(CustomSoundKey, value); Raise(nameof(CustomSoundPath)); }
    }

    public bool ShowSessionDetails
    {
        get => _showSessionDetails;
        set { _showSessionDetails = value; Preferences.Set(ShowDetailsKey, value); Raise(nameof(ShowSessionDetails)); }
    }

    /// Resolves the current choice to a playable file, or null when nothing
    /// usable exists (sound then simply stays silent).
    public string? ResolveSoundFile()
    {
        if (_soundChoice == CustomSoundChoice)
        {
            return File.Exists(_customSoundPath) ? _customSoundPath : null;
        }
        var media = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media", _soundChoice + ".wav");
        return File.Exists(media) ? media : null;
    }

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

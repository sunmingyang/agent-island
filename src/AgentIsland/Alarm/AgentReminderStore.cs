using System.ComponentModel;
using System.IO;
using AgentIsland.Core;

namespace AgentIsland.Alarm;

/// Turn-alarm preferences: master switch, sound on/off, volume, sound
/// choice (Windows Media presets or a custom file), and whether the alarm
/// window shows session details.
public sealed class AgentReminderStore : INotifyPropertyChanged
{
    /// Built-in presets, synthesized to match the character of the macOS
    /// alert-sound palette (Basso, Blow, Bottle, …). Keys are stable; the
    /// display names localize (低音, 吹气, 瓶子, …). Declared before Shared:
    /// static initializers run in declaration order, and the instance ctor
    /// reads this array.
    public static readonly string[] SoundPresets =
    {
        "Basso", "Blow", "Bottle", "Frog", "Glass", "Hero", "Ping", "Submarine",
    };

    public static AgentReminderStore Shared { get; } = new();

    private const string EnabledKey = "AgentIsland.agentReminders";
    private const string SoundEnabledKey = "AgentIsland.agentReminderSound";
    private const string VolumeKey = "AgentIsland.agentReminderVolume";
    private const string SoundChoiceKey = "AgentIsland.agentReminderSoundChoice";
    private const string CustomSoundKey = "AgentIsland.agentReminderCustomSound";
    private const string ShowDetailsKey = "AgentIsland.agentReminderShowSessionDetails";

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
        _soundChoice = Preferences.Get<string?>(SoundChoiceKey) ?? "Glass";
        // Early builds referenced Windows Media names; fold them into the
        // synthesized palette.
        if (_soundChoice != CustomSoundChoice && !SoundPresets.Contains(_soundChoice))
        {
            _soundChoice = "Glass";
        }
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
    /// usable exists (sound then simply stays silent). Presets synthesize
    /// on first use.
    public string? ResolveSoundFile()
    {
        if (_soundChoice == CustomSoundChoice)
        {
            return File.Exists(_customSoundPath) ? _customSoundPath : null;
        }
        return SoundSynth.EnsurePreset(_soundChoice);
    }

    /// Localized display name for a preset key — the zh names mirror the
    /// macOS sound list (低音, 吹气, 瓶子, …).
    public static string PresetLabel(string key)
    {
        if (!Localization.L10n.IsChinese) return key;
        return key switch
        {
            "Basso" => "低音",
            "Blow" => "吹气",
            "Bottle" => "瓶子",
            "Frog" => "青蛙",
            "Glass" => "玻璃",
            "Hero" => "英雄",
            "Ping" => "叮",
            "Submarine" => "水下",
            _ => key,
        };
    }

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

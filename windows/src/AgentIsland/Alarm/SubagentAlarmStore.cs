using System.ComponentModel;
using AgentIsland.Core;

namespace AgentIsland.Alarm;

/// Whether orchestrated subagents raise turn alarms. Agent fan-outs finish
/// dozens of machine-driven threads per prompt — Claude Task/agent
/// transcripts and Codex spawned/review threads — and none of them is the
/// user's turn, so this defaults OFF and only main threads alarm.
public sealed class SubagentAlarmStore : INotifyPropertyChanged
{
    public static SubagentAlarmStore Shared { get; } = new();

    private const string EnabledKey = "AgentIsland.subagentAlarms";

    private bool _enabled;

    public event PropertyChangedEventHandler? PropertyChanged;

    private SubagentAlarmStore()
    {
        _enabled = Preferences.Get<bool?>(EnabledKey) ?? false;
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

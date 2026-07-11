using System.ComponentModel;
using AgentIsland.Core;

namespace AgentIsland.Model;

/// How "Open thread" treats a Claude *Desktop* session: land in the Desktop
/// app (default), or resume the exact conversation in a terminal via
/// `claude --resume`. Desktop can't jump to a specific chat yet — its
/// `claude://code/<bridge-id>` route sits behind Anthropic's server-side
/// flag — so people who value "exact conversation" over "my usual window"
/// can pick the CLI here. (Parity with the macOS store.)
public sealed class ClaudeJumpPreferenceStore : INotifyPropertyChanged
{
    public static ClaudeJumpPreferenceStore Shared { get; } = new();

    private const string Key = "AgentIsland.claudeJumpPrefersCli";

    private bool _prefersCli;

    public event PropertyChangedEventHandler? PropertyChanged;

    private ClaudeJumpPreferenceStore()
    {
        _prefersCli = Preferences.Get<bool?>(Key) ?? false;
    }

    public bool PrefersCli
    {
        get => _prefersCli;
        set
        {
            if (_prefersCli == value) return;
            _prefersCli = value;
            Preferences.Set(Key, value);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PrefersCli)));
        }
    }
}

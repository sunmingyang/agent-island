using System.ComponentModel;
using AgentIsland.Core;

namespace AgentIsland.Model;

/// How "Open thread" treats a Codex session: deep-link into the desktop
/// app's exact thread (default — codex://threads/<id> is the app's own
/// official route), or reopen the conversation in a terminal via
/// `codex resume`, for people who live in the CLI. (Parity with macOS.)
public sealed class CodexJumpPreferenceStore : INotifyPropertyChanged
{
    public static CodexJumpPreferenceStore Shared { get; } = new();

    private const string Key = "AgentIsland.codexJumpPrefersCli";

    private bool _prefersCli;

    public event PropertyChangedEventHandler? PropertyChanged;

    private CodexJumpPreferenceStore()
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

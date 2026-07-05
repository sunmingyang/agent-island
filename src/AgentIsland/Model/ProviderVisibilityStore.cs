using System.ComponentModel;
using AgentIsland.Core;

namespace AgentIsland.Model;

public sealed class ProviderVisibilityStore : INotifyPropertyChanged
{
    public static ProviderVisibilityStore Shared { get; } = new();

    private bool _claudeVisible;
    private bool _codexVisible;

    public event PropertyChangedEventHandler? PropertyChanged;

    private ProviderVisibilityStore()
    {
        _claudeVisible = Preferences.Get<bool?>("MacIsland.claudeVisible") ?? true;
        _codexVisible = Preferences.Get<bool?>("MacIsland.codexVisible") ?? true;
    }

    public bool ClaudeVisible
    {
        get => _claudeVisible;
        set
        {
            _claudeVisible = value;
            Preferences.Set("MacIsland.claudeVisible", value);
            Raise(nameof(ClaudeVisible));
        }
    }

    public bool CodexVisible
    {
        get => _codexVisible;
        set
        {
            _codexVisible = value;
            Preferences.Set("MacIsland.codexVisible", value);
            Raise(nameof(CodexVisible));
        }
    }

    public bool IsVisible(TriggerTool tool) => tool == TriggerTool.Claude ? _claudeVisible : _codexVisible;

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

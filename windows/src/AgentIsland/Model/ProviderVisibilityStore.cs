using System.ComponentModel;
using System.IO;
using AgentIsland.Core;

namespace AgentIsland.Model;

/// Which provider halves the island shows. Two layers, mirroring macOS:
///   - the manual Settings toggles (ClaudeVisible / CodexVisible);
///   - machine detection (ClaudeDetected / CodexDetected): a provider with
///     no CLI footprint on this machine auto-yields its half, so a
///     single-subscription user gets the solo layout without hunting for
///     the toggle.
/// Manual wins once touched: flipping a toggle records intent, and
/// detection stops second-guessing that provider.
public sealed class ProviderVisibilityStore : INotifyPropertyChanged
{
    private const string ClaudeTouchedKey = "MacIsland.claudeVisibleTouched";
    private const string CodexTouchedKey = "MacIsland.codexVisibleTouched";

    public static ProviderVisibilityStore Shared { get; } = new();

    private bool _claudeVisible;
    private bool _codexVisible;

    public event PropertyChangedEventHandler? PropertyChanged;

    private ProviderVisibilityStore()
    {
        _claudeVisible = Preferences.Get<bool?>("MacIsland.claudeVisible") ?? true;
        _codexVisible = Preferences.Get<bool?>("MacIsland.codexVisible") ?? true;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        ClaudeDetected = HasDir(home, ".claude") || HasDir(home, ".config", "claude");
        CodexDetected = HasDir(home, ".codex");
    }

    /// CLI footprint on this machine, probed once at launch.
    public bool ClaudeDetected { get; }
    public bool CodexDetected { get; }

    public bool ClaudeVisible
    {
        get => _claudeVisible;
        set
        {
            _claudeVisible = value;
            Preferences.Set("MacIsland.claudeVisible", value);
            Preferences.Set(ClaudeTouchedKey, true);
            Raise(nameof(ClaudeVisible));
            Raise(nameof(ClaudeShown));
        }
    }

    public bool CodexVisible
    {
        get => _codexVisible;
        set
        {
            _codexVisible = value;
            Preferences.Set("MacIsland.codexVisible", value);
            Preferences.Set(CodexTouchedKey, true);
            Raise(nameof(CodexVisible));
            Raise(nameof(CodexShown));
        }
    }

    /// What the island actually renders. Detection only speaks for a
    /// provider whose toggle was never touched, and only hides one side
    /// when the OTHER side is present — a machine with neither footprint
    /// still shows both instead of a blank island.
    public bool ClaudeShown
    {
        get
        {
            if (!_claudeVisible) return false;
            if (Preferences.Get<bool?>(ClaudeTouchedKey) == true) return true;
            return ClaudeDetected || !CodexDetected;
        }
    }

    public bool CodexShown
    {
        get
        {
            if (!_codexVisible) return false;
            if (Preferences.Get<bool?>(CodexTouchedKey) == true) return true;
            return CodexDetected || !ClaudeDetected;
        }
    }

    public bool IsVisible(TriggerTool tool) => tool == TriggerTool.Claude ? ClaudeShown : CodexShown;

    private static bool HasDir(params string[] parts) => Directory.Exists(Path.Combine(parts));

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

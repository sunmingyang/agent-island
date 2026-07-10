using System.ComponentModel;
using AgentIsland.Core;

namespace AgentIsland.UI;

public enum IslandScreen
{
    Usage,
    Cost,
    Overview,
    Triggers,
}

/// Which carousel page is showing. Persisted so the island reopens on the
/// page the user last used.
public sealed class ScreenPref : INotifyPropertyChanged
{
    private const string Key = "MacIsland.screen";
    private const string SwipedKey = "MacIsland.hasSwipedScreen";

    public static ScreenPref Shared { get; } = new();

    private IslandScreen _screen;
    private bool _hasSwiped;
    private bool _showCostPage;

    public event PropertyChangedEventHandler? PropertyChanged;

    private ScreenPref()
    {
        var raw = Preferences.Get<string?>(Key);
        _screen = Enum.TryParse<IslandScreen>(raw, ignoreCase: true, out var parsed)
            ? parsed
            : IslandScreen.Usage;
        _hasSwiped = Preferences.Get<bool?>(SwipedKey) ?? false;
        // Cost page hidden by default (parity with macOS CostPanelVisibilityStore).
        // Users opt in via Settings → Display; anyone who already toggled it keeps
        // their stored choice. The Screen guard below sends a new install to Usage
        // rather than an invisible Cost page.
        _showCostPage = Preferences.Get<bool?>("MacIsland.showCostPanelPage") ?? false;
        if (!VisibleScreens.Contains(_screen)) _screen = IslandScreen.Usage;
    }

    public IslandScreen Screen
    {
        get => _screen;
        set
        {
            if (_screen == value) return;
            _screen = value;
            Preferences.Set(Key, value.ToString());
            Raise(nameof(Screen));
        }
    }

    public bool HasSwiped
    {
        get => _hasSwiped;
        set
        {
            if (_hasSwiped == value) return;
            _hasSwiped = value;
            Preferences.Set(SwipedKey, value);
            Raise(nameof(HasSwiped));
        }
    }

    /// "Show cost page in top panel" (Settings → Display).
    public bool ShowCostPage
    {
        get => _showCostPage;
        set
        {
            if (_showCostPage == value) return;
            _showCostPage = value;
            Preferences.Set("MacIsland.showCostPanelPage", value);
            if (!VisibleScreens.Contains(_screen)) Screen = IslandScreen.Usage;
            Raise(nameof(ShowCostPage));
            Raise(nameof(VisibleScreens));
        }
    }

    public IReadOnlyList<IslandScreen> VisibleScreens => _showCostPage
        ? new[] { IslandScreen.Usage, IslandScreen.Cost, IslandScreen.Overview, IslandScreen.Triggers }
        : new[] { IslandScreen.Usage, IslandScreen.Overview, IslandScreen.Triggers };

    public void ShowNext(int direction)
    {
        var screens = VisibleScreens;
        var index = screens.ToList().IndexOf(_screen);
        var next = Math.Clamp(index + direction, 0, screens.Count - 1);
        if (screens[next] != _screen)
        {
            HasSwiped = true;
            Screen = screens[next];
        }
    }

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

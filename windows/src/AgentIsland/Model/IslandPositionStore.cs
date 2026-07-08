using System.ComponentModel;
using AgentIsland.Core;

namespace AgentIsland.Model;

/// How and where the island sits on screen. Windows has no notch reserving
/// the top-center, so placement is a user choice with several native-feeling
/// modes, unlike macOS which always pins top-center over the notch.
public enum IslandPlacement
{
    /// Horizontal bar hugging the top edge (the Mac look).
    TopBar,
    /// Horizontal bar on the bottom work-area edge, above the taskbar.
    BottomBar,
    /// A free-floating widget the user drags anywhere; its position sticks.
    Floating,
    /// A vertical rail hugging the left edge; expands toward screen center.
    LeftRail,
    /// A vertical rail hugging the right edge; expands toward screen center.
    RightRail,
    /// Docked into the bottom-right corner beside the notification tray.
    Tray,
}

/// Where along a horizontal bar edge the island sits (TopBar/BottomBar only).
/// Center is the signature look; Left/Right clear browser tabs and title-bar
/// buttons that live in the top-center of maximized windows.
public enum IslandAlignment
{
    Left,
    Center,
    Right,
}

/// Persisted island placement. Windows-only concept, hence the AgentIsland.
/// key prefix rather than the ported MacIsland. namespace.
public sealed class IslandPositionStore : INotifyPropertyChanged
{
    private const string PlacementKey = "AgentIsland.islandPlacement";
    private const string AlignmentKey = "AgentIsland.islandAlignment";
    private const string FloatXKey = "AgentIsland.floatX";
    private const string FloatYKey = "AgentIsland.floatY";
    // Legacy key from the first edge/alignment iteration.
    private const string LegacyEdgeKey = "AgentIsland.islandEdge";

    public static IslandPositionStore Shared { get; } = new();

    private IslandPlacement _placement;
    private IslandAlignment _alignment;
    private double? _floatX;
    private double? _floatY;

    public event PropertyChangedEventHandler? PropertyChanged;

    private IslandPositionStore()
    {
        if (Enum.TryParse<IslandPlacement>(Preferences.Get<string?>(PlacementKey), out var placement))
        {
            _placement = placement;
        }
        else
        {
            // Migrate the old Top/Bottom edge choice into the new modes.
            _placement = Preferences.Get<string?>(LegacyEdgeKey) == "Bottom"
                ? IslandPlacement.BottomBar
                : IslandPlacement.TopBar;
        }
        _alignment = Enum.TryParse<IslandAlignment>(Preferences.Get<string?>(AlignmentKey), out var alignment)
            ? alignment
            : IslandAlignment.Center;
        _floatX = Preferences.Get<double?>(FloatXKey);
        _floatY = Preferences.Get<double?>(FloatYKey);
    }

    public IslandPlacement Placement
    {
        get => _placement;
        set
        {
            if (_placement == value) return;
            _placement = value;
            Preferences.Set(PlacementKey, value.ToString());
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Placement)));
        }
    }

    public IslandAlignment Alignment
    {
        get => _alignment;
        set
        {
            if (_alignment == value) return;
            _alignment = value;
            Preferences.Set(AlignmentKey, value.ToString());
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Alignment)));
        }
    }

    /// True when the island lays out as a horizontal bar (bar/floating/tray).
    public bool IsHorizontal => _placement is not (IslandPlacement.LeftRail or IslandPlacement.RightRail);

    /// The persisted floating top-left in DIP, or null until first dragged.
    public (double X, double Y)? FloatingPoint =>
        _floatX is { } x && _floatY is { } y ? (x, y) : null;

    /// Persist a dragged floating position; silently ignored unless the
    /// island is actually in Floating mode (a drag in another mode is noise).
    public void SetFloatingPoint(double x, double y)
    {
        _floatX = x;
        _floatY = y;
        Preferences.Set(FloatXKey, x);
        Preferences.Set(FloatYKey, y);
    }
}

using System.ComponentModel;
using AgentIsland.Core;

namespace AgentIsland.Model;

/// Which screen edge the island docks to. Windows has no notch reserving
/// the top-center, so unlike macOS the edge is a user choice: Top mirrors
/// the Mac look; Bottom sits the island on the work area, above the taskbar.
public enum IslandEdge
{
    Top,
    Bottom,
}

/// Where along the docked edge the island sits. Center is the signature
/// look; Left/Right clear browser tabs and title-bar buttons that live in
/// the top-center of maximized windows.
public enum IslandAlignment
{
    Left,
    Center,
    Right,
}

/// Persisted island placement. Windows-only concept (macOS always pins
/// top-center over the notch), hence the AgentIsland. key prefix rather
/// than the ported MacIsland. namespace.
public sealed class IslandPositionStore : INotifyPropertyChanged
{
    private const string EdgeKey = "AgentIsland.islandEdge";
    private const string AlignmentKey = "AgentIsland.islandAlignment";

    public static IslandPositionStore Shared { get; } = new();

    private IslandEdge _edge;
    private IslandAlignment _alignment;

    public event PropertyChangedEventHandler? PropertyChanged;

    private IslandPositionStore()
    {
        _edge = Enum.TryParse<IslandEdge>(Preferences.Get<string?>(EdgeKey), out var edge)
            ? edge
            : IslandEdge.Top;
        _alignment = Enum.TryParse<IslandAlignment>(Preferences.Get<string?>(AlignmentKey), out var alignment)
            ? alignment
            : IslandAlignment.Center;
    }

    public IslandEdge Edge
    {
        get => _edge;
        set
        {
            if (_edge == value) return;
            _edge = value;
            Preferences.Set(EdgeKey, value.ToString());
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Edge)));
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
}

using System.ComponentModel;
using System.Windows;

namespace AgentIsland.UI;

public enum IslandState
{
    Compact,
    Peek,
    Expanded,
}

/// Island bar width preset. macOS offered a wide notch-style bar and a
/// compact one; Windows has no camera notch, so the wide style (matching the
/// product's signature look) is the default and compact remains for users
/// who want a smaller pill.
public enum IslandSpacingMode
{
    NotchStyle,
    Compact,
}

/// State machine + geometry for the island silhouette. Sizes are the shipped
/// macOS constants: tab 38, peek pill slot 104 per side, expanded panel 800
/// wide with 188/244pt content pages.
public sealed class IslandModel : INotifyPropertyChanged
{
    public static IslandModel Shared { get; } = new();

    public const double TabWidth = 38;
    public const double PillSlotWidth = 104;
    public const double ExpandedWidth = 800;
    /// Windows counterpart of the menu-bar-height silhouette (37pt notched
    /// Mac, 24pt compact): one value tuned for taskbar-less screen tops.
    public const double SilhouetteHeight = 36;
    public const double UsageContentHeight = 188;
    public const double OverviewContentHeight = 244;
    public const double OverviewDetailHeight = 52;
    /// Bottom corner radius; top corners stay square against the screen edge.
    public const double CompactCornerRadius = 14;
    public const double ExpandedCornerRadius = 24;

    private IslandState _state = IslandState.Compact;
    private IslandSpacingMode _spacingMode = IslandSpacingMode.NotchStyle;
    private double _expandedContentHeight = UsageContentHeight;

    public event PropertyChangedEventHandler? PropertyChanged;

    public IslandState State
    {
        get => _state;
        set
        {
            if (_state == value) return;
            _state = value;
            Raise(nameof(State));
            Raise(nameof(Size));
        }
    }

    public IslandSpacingMode SpacingMode
    {
        get => _spacingMode;
        set
        {
            if (_spacingMode == value) return;
            _spacingMode = value;
            Raise(nameof(SpacingMode));
            Raise(nameof(Size));
        }
    }

    /// Height of the expanded content area below the silhouette strip;
    /// depends on the visible page (usage/cost 188, overview 244 + detail).
    public double ExpandedContentHeight
    {
        get => _expandedContentHeight;
        set
        {
            if (Math.Abs(_expandedContentHeight - value) < 0.5) return;
            _expandedContentHeight = value;
            Raise(nameof(ExpandedContentHeight));
            Raise(nameof(Size));
        }
    }

    public double NotchWidth => _spacingMode == IslandSpacingMode.NotchStyle ? 200 : 100;

    public Size Size => _state switch
    {
        IslandState.Compact => new Size(NotchWidth + TabWidth * 2, SilhouetteHeight),
        IslandState.Peek => new Size(NotchWidth + (TabWidth + PillSlotWidth) * 2, SilhouetteHeight),
        IslandState.Expanded => new Size(ExpandedWidth, SilhouetteHeight + _expandedContentHeight),
        _ => new Size(NotchWidth + TabWidth * 2, SilhouetteHeight),
    };

    public double CornerRadius => _state == IslandState.Expanded ? ExpandedCornerRadius : CompactCornerRadius;

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

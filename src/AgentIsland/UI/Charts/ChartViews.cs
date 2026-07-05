using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using AgentIsland.UI.Theme;
using AgentIsland.Usage;

namespace AgentIsland.UI.Charts;

/// Shared head for the "label + big number" charts: window label on the
/// left (lowercase, 55% white), percent value on the right in mono
/// semibold tinted by urgency, with a quiet "%" suffix.
public sealed class ChartHead : Grid
{
    private readonly TextBlock _label;
    private readonly TextBlock _value;
    private readonly TextBlock _suffix;

    public ChartHead()
    {
        _label = new TextBlock
        {
            FontFamily = IslandFonts.Ui,
            FontSize = 11,
            FontWeight = FontWeights.Medium,
            Foreground = IslandColors.Brush(IslandColors.White(0.55)),
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        _value = new TextBlock
        {
            FontFamily = IslandFonts.Mono,
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        _suffix = new TextBlock
        {
            Text = "%",
            FontFamily = IslandFonts.Ui,
            FontSize = 11,
            FontWeight = FontWeights.Medium,
            Foreground = IslandColors.Brush(IslandColors.White(0.5)),
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(1, 0, 0, 1),
        };
        var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        right.Children.Add(_value);
        right.Children.Add(_suffix);
        Children.Add(_label);
        Children.Add(right);
    }

    public void Update(string label, double value)
    {
        _label.Text = label.ToLowerInvariant();
        _value.Text = $"{(int)value}";
        _value.Foreground = IslandColors.Brush(IslandColors.Urgency(value / 100));
    }
}

public sealed class ChartFoot : TextBlock
{
    public ChartFoot()
    {
        FontFamily = IslandFonts.Mono;
        FontSize = 10;
        Foreground = IslandColors.Brush(IslandColors.White(0.4));
        TextTrimming = TextTrimming.CharacterEllipsis;
    }
}

/// 30-cell segmented meter, the signature look from the product
/// screenshots. Filled cells take the provider color; a value change sweeps
/// across cells with a ~7ms stagger (~210ms full sweep).
public sealed class SteppedMeter : Grid
{
    private const int Segments = 30;
    private readonly Rectangle[] _cells = new Rectangle[Segments];
    private readonly Color _color;
    private double _lastFilled = -1;

    public SteppedMeter(Color color)
    {
        _color = color;
        Height = 16;
        ColumnDefinitions.Clear();
        for (var i = 0; i < Segments; i++)
        {
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var cell = new Rectangle
            {
                RadiusX = 1.5,
                RadiusY = 1.5,
                Margin = new Thickness(i == 0 ? 0 : 1, 0, i == Segments - 1 ? 0 : 1, 0),
                Fill = IslandColors.Brush(IslandColors.White(0.10)),
            };
            SetColumn(cell, i);
            _cells[i] = cell;
            Children.Add(cell);
        }
    }

    public void Update(double value)
    {
        var filled = Math.Floor(value / 100 * Segments);
        if (Math.Abs(filled - _lastFilled) < 0.5 && _lastFilled >= 0)
        {
            return;
        }
        _lastFilled = filled;
        for (var i = 0; i < Segments; i++)
        {
            var target = i < filled ? _color : IslandColors.White(0.10);
            var brush = new SolidColorBrush(((SolidColorBrush)_cells[i].Fill).Color);
            _cells[i].Fill = brush;
            var animation = new ColorAnimation(target, IslandAnimations.StrongEaseOutDuration)
            {
                BeginTime = TimeSpan.FromMilliseconds(i * 7),
                EasingFunction = IslandAnimations.StrongEaseOut(),
            };
            brush.BeginAnimation(SolidColorBrush.ColorProperty, animation);
        }
    }
}

/// Thin capsule progress bar with quartile ticks (the "Bar" style).
public sealed class CapsuleMeter : Grid
{
    private readonly Border _fill;
    private double _value;

    public CapsuleMeter(Color color)
    {
        Height = 8;
        var track = new Border
        {
            Height = 4,
            CornerRadius = new CornerRadius(2),
            Background = IslandColors.Brush(IslandColors.White(0.06)),
            VerticalAlignment = VerticalAlignment.Center,
        };
        _fill = new Border
        {
            Height = 4,
            CornerRadius = new CornerRadius(2),
            Background = IslandColors.Brush(color),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Width = 0,
        };
        Children.Add(track);
        Children.Add(_fill);
        for (var quartile = 1; quartile <= 3; quartile++)
        {
            var tick = new Rectangle
            {
                Width = 1,
                Height = 8,
                Fill = IslandColors.Brush(IslandColors.White(0.12)),
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            var p = quartile * 0.25;
            SizeChanged += (_, _) => tick.Margin = new Thickness(ActualWidth * p, 0, 0, 0);
            Children.Add(tick);
        }
        SizeChanged += (_, _) => ApplyWidth(animate: false);
    }

    public void Update(double value)
    {
        _value = value;
        ApplyWidth(animate: true);
    }

    private void ApplyWidth(bool animate)
    {
        var target = Math.Max(0, ActualWidth * _value / 100);
        if (!animate)
        {
            _fill.BeginAnimation(WidthProperty, null);
            _fill.Width = target;
            return;
        }
        var animation = new DoubleAnimation(target, IslandAnimations.StrongEaseOutDuration)
        {
            EasingFunction = IslandAnimations.StrongEaseOut(),
        };
        _fill.BeginAnimation(WidthProperty, animation);
    }
}

/// One usage window tile: head, meter (per style), reset caption. Locked
/// height so the panel size is identical regardless of the chosen style.
public sealed class ChartTile : StackPanel
{
    public const double TileHeight = 96;

    private readonly ChartHead _head = new();
    private readonly ChartFoot _foot = new();
    private readonly SteppedMeter _stepped;
    private readonly CapsuleMeter _capsule;
    private readonly string _labelKey;

    public ChartTile(Color color, string labelKey)
    {
        _labelKey = labelKey;
        Orientation = Orientation.Vertical;
        Height = TileHeight;
        _stepped = new SteppedMeter(color) { Margin = new Thickness(0, 8, 0, 8) };
        _capsule = new CapsuleMeter(color) { Margin = new Thickness(0, 12, 0, 12) };
        Children.Add(_head);
        Children.Add(_stepped);
        Children.Add(_capsule);
        Children.Add(_foot);
    }

    public void Update(WindowUsage window, ChartStyle style)
    {
        var value = window.UsedPercent * 100;
        _head.Update(Localization.L10n.Tr(_labelKey), value);
        _stepped.Visibility = style == ChartStyle.Stepped ? Visibility.Visible : Visibility.Collapsed;
        _capsule.Visibility = style == ChartStyle.Stepped ? Visibility.Collapsed : Visibility.Visible;
        if (style == ChartStyle.Stepped) _stepped.Update(value); else _capsule.Update(value);
        _foot.Text = SubCaption(window);
    }

    /// "no data" is the internal sentinel for "API returned null for this
    /// window" — hide it so the tile reads as a passive window-context cue.
    /// Real errors surface before reset countdowns because preserved stale
    /// values may carry an old resetAt that would otherwise render as "0s".
    private static string SubCaption(WindowUsage window)
    {
        if (window.Error is { } error && error != "no data")
        {
            if (ClaudeCredentials.IsAuthRecoverableError(error) && ClaudeCredentials.CanPromptReauth())
            {
                return "";
            }
            return error;
        }
        if (window.ResetAt is { } resetAt)
        {
            var delta = resetAt - DateTimeOffset.Now;
            if (delta < TimeSpan.Zero) delta = TimeSpan.Zero;
            return Localization.L10n.TrFormat("resets in {0}", Core.Formatting.CompactDuration(delta));
        }
        return "";
    }
}

public static class IslandFonts
{
    public static readonly FontFamily Ui = new("Segoe UI Variable Text, Segoe UI");
    public static readonly FontFamily Mono = new("Cascadia Mono, Consolas");
}

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

/// Circular progress ring with the percent in its center and the window
/// label beside it (the "Ring" style).
public sealed class RingMeter : Grid
{
    private readonly System.Windows.Shapes.Path _progress;
    private readonly TextBlock _center;
    private readonly TextBlock _label;
    private const double Diameter = 56;
    private const double Stroke = 5;

    public RingMeter(Color color)
    {
        Height = 64;
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var host = new Grid { Width = Diameter, Height = Diameter, VerticalAlignment = VerticalAlignment.Center };
        host.Children.Add(new System.Windows.Shapes.Ellipse
        {
            Stroke = IslandColors.Brush(IslandColors.White(0.08)),
            StrokeThickness = Stroke,
        });
        _progress = new System.Windows.Shapes.Path
        {
            Stroke = IslandColors.Brush(color),
            StrokeThickness = Stroke,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
        };
        host.Children.Add(_progress);
        _center = new TextBlock
        {
            FontFamily = IslandFonts.Mono,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        host.Children.Add(_center);
        SetColumn(host, 0);
        Children.Add(host);

        _label = new TextBlock
        {
            FontFamily = IslandFonts.Ui,
            FontSize = 11,
            FontWeight = FontWeights.Medium,
            Foreground = IslandColors.Brush(IslandColors.White(0.55)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0),
        };
        SetColumn(_label, 1);
        Children.Add(_label);
    }

    public void Update(string label, double value)
    {
        _label.Text = label.ToLowerInvariant();
        _center.Text = $"{(int)value}%";
        _center.Foreground = IslandColors.Brush(IslandColors.Urgency(value / 100));
        _progress.Data = ArcGeometry(Math.Clamp(value, 0, 100) / 100 * 359.9);
    }

    private static Geometry ArcGeometry(double sweepDegrees)
    {
        var radius = (Diameter - Stroke) / 2;
        var center = new Point(Diameter / 2, Diameter / 2);
        var start = new Point(center.X, center.Y - radius);
        var angle = sweepDegrees * Math.PI / 180;
        var end = new Point(
            center.X + radius * Math.Sin(angle),
            center.Y - radius * Math.Cos(angle));
        var figure = new PathFigure { StartPoint = start, IsClosed = false };
        figure.Segments.Add(new ArcSegment(
            end,
            new Size(radius, radius),
            0,
            sweepDegrees > 180,
            SweepDirection.Clockwise,
            true));
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }
}

/// Numbers-first style: oversized percent, window label, reset caption.
public sealed class NumericMeter : StackPanel
{
    private readonly TextBlock _value;
    private readonly TextBlock _label;

    public NumericMeter()
    {
        Orientation = Orientation.Vertical;
        _value = new TextBlock
        {
            FontFamily = IslandFonts.Mono,
            FontSize = 30,
            FontWeight = FontWeights.SemiBold,
        };
        _label = new TextBlock
        {
            FontFamily = IslandFonts.Ui,
            FontSize = 11,
            FontWeight = FontWeights.Medium,
            Foreground = IslandColors.Brush(IslandColors.White(0.55)),
            Margin = new Thickness(1, 2, 0, 0),
        };
        Children.Add(_value);
        Children.Add(_label);
    }

    public void Update(string label, double value)
    {
        _value.Inlines.Clear();
        _value.Inlines.Add(new System.Windows.Documents.Run($"{(int)value}")
        {
            Foreground = IslandColors.Brush(IslandColors.Urgency(value / 100)),
        });
        _value.Inlines.Add(new System.Windows.Documents.Run("%")
        {
            FontSize = 14,
            Foreground = IslandColors.Brush(IslandColors.White(0.5)),
        });
        _label.Text = label.ToLowerInvariant();
    }
}

/// Seeded waveform bars; the filled fraction tracks the percent (the
/// "Spark" style).
public sealed class SparkMeter : Grid
{
    private const int Bars = 24;
    private readonly System.Windows.Shapes.Rectangle[] _bars = new System.Windows.Shapes.Rectangle[Bars];
    private readonly double[] _heights = new double[Bars];
    private readonly Color _color;

    public SparkMeter(Color color, int seed)
    {
        _color = color;
        Height = 24;
        for (var i = 0; i < Bars; i++)
        {
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            // Deterministic per-seed wave so the shape is stable frame to frame.
            _heights[i] = 8 + 14 * Math.Abs(Math.Sin(i * 0.82 + seed * 1.7) * 0.7 + Math.Sin(i * 0.31 + seed) * 0.3);
            var bar = new System.Windows.Shapes.Rectangle
            {
                RadiusX = 1,
                RadiusY = 1,
                Height = Math.Min(22, _heights[i]),
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(i == 0 ? 0 : 1, 0, i == Bars - 1 ? 0 : 1, 0),
                Fill = IslandColors.Brush(IslandColors.White(0.10)),
            };
            SetColumn(bar, i);
            _bars[i] = bar;
            Children.Add(bar);
        }
    }

    public void Update(double value)
    {
        var filled = value / 100 * Bars;
        for (var i = 0; i < Bars; i++)
        {
            _bars[i].Fill = i < filled
                ? IslandColors.Brush(_color)
                : IslandColors.Brush(IslandColors.White(0.10));
        }
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
    private readonly RingMeter _ring;
    private readonly NumericMeter _numeric;
    private readonly SparkMeter _spark;
    private readonly string _labelKey;

    public ChartTile(Color color, string labelKey, int seed = 1)
    {
        _labelKey = labelKey;
        Orientation = Orientation.Vertical;
        Height = TileHeight;
        _stepped = new SteppedMeter(color) { Margin = new Thickness(0, 8, 0, 8) };
        _capsule = new CapsuleMeter(color) { Margin = new Thickness(0, 12, 0, 12) };
        _ring = new RingMeter(color) { Margin = new Thickness(0, 4, 0, 4) };
        _numeric = new NumericMeter { Margin = new Thickness(0, 2, 0, 2) };
        _spark = new SparkMeter(color, seed) { Margin = new Thickness(0, 6, 0, 6) };
        Children.Add(_head);
        Children.Add(_stepped);
        Children.Add(_capsule);
        Children.Add(_ring);
        Children.Add(_numeric);
        Children.Add(_spark);
        Children.Add(_foot);
    }

    public void Update(WindowUsage window, ChartStyle style)
    {
        var value = window.UsedPercent * 100;
        var label = Localization.L10n.Tr(_labelKey);

        // Ring and Numeric render their own heads; the shared head serves
        // the three label+number styles.
        _head.Visibility = style is ChartStyle.Bar or ChartStyle.Stepped or ChartStyle.Spark
            ? Visibility.Visible
            : Visibility.Collapsed;
        _stepped.Visibility = style == ChartStyle.Stepped ? Visibility.Visible : Visibility.Collapsed;
        _capsule.Visibility = style == ChartStyle.Bar ? Visibility.Visible : Visibility.Collapsed;
        _ring.Visibility = style == ChartStyle.Ring ? Visibility.Visible : Visibility.Collapsed;
        _numeric.Visibility = style == ChartStyle.Numeric ? Visibility.Visible : Visibility.Collapsed;
        _spark.Visibility = style == ChartStyle.Spark ? Visibility.Visible : Visibility.Collapsed;

        switch (style)
        {
            case ChartStyle.Stepped:
                _head.Update(label, value);
                _stepped.Update(value);
                break;
            case ChartStyle.Bar:
                _head.Update(label, value);
                _capsule.Update(value);
                break;
            case ChartStyle.Ring:
                _ring.Update(label, value);
                break;
            case ChartStyle.Numeric:
                _numeric.Update(label, value);
                break;
            case ChartStyle.Spark:
                _head.Update(label, value);
                _spark.Update(value);
                break;
        }
        _foot.Text = SubCaption(window, style);
    }

    /// "no data" is the internal sentinel for "API returned null for this
    /// window" — hide it so the tile reads as a passive window-context cue.
    /// Real errors surface before reset countdowns because preserved stale
    /// values may carry an old resetAt that would otherwise render as "0s".
    private static string SubCaption(WindowUsage window, ChartStyle style)
    {
        if (window.Error is { } error && error != "no data")
        {
            // The inline re-auth button below the tiles carries the
            // remediation; repeating it in both captions reads twice.
            if (ClaudeCredentials.IsAuthRecoverableError(error))
            {
                return "";
            }
            return Localization.ErrorDisplay.Localize(error);
        }
        if (window.ResetAt is { } resetAt)
        {
            var delta = resetAt - DateTimeOffset.Now;
            if (delta < TimeSpan.Zero) delta = TimeSpan.Zero;
            var compact = Core.Formatting.CompactDuration(delta);
            return style == ChartStyle.Numeric
                ? "↻ " + compact
                : Localization.L10n.TrFormat("resets in {0}", compact);
        }
        return "";
    }
}

public static class IslandFonts
{
    public static readonly FontFamily Ui = new("Segoe UI Variable Text, Segoe UI");
    public static readonly FontFamily Mono = new("Cascadia Mono, Consolas");
}

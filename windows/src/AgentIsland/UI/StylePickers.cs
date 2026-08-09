using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using AgentIsland.UI.Charts;
using AgentIsland.UI.Theme;

namespace AgentIsland.UI;

/// The visual chart-style picker from the macOS Display tab: five preview
/// tiles (ring / bar / stepped / numeric / spark) drawn in the Claude
/// terracotta, the selected one framed in blue.
public sealed class ChartStylePickerControl : Grid
{
    private static readonly Color SelectionBlue = Color.FromRgb(0x2E, 0x7C, 0xF6);
    private readonly List<Border> _tiles = new();
    private readonly List<TextBlock> _labels = new();

    public event Action<ChartStyle>? StyleSelected;

    public ChartStylePickerControl(ChartStyle selected)
    {
        var styles = Enum.GetValues<ChartStyle>();
        for (var i = 0; i < styles.Length; i++)
        {
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }
        for (var i = 0; i < styles.Length; i++)
        {
            var style = styles[i];
            var tile = MakeTile(style);
            SetColumn(tile, i);
            _tiles.Add(tile);
            Children.Add(tile);
        }
        Select(selected);
    }

    private Border MakeTile(ChartStyle style)
    {
        var stack = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var preview = new Grid { Height = 40, Width = 72, Margin = new Thickness(0, 0, 0, 10) };
        preview.Children.Add(MakePreview(style));
        stack.Children.Add(preview);
        var label = new TextBlock
        {
            Text = StyleLabel(style),
            FontFamily = IslandFonts.Ui,
            FontSize = 12,
            FontWeight = FontWeights.Medium,
            Foreground = IslandColors.Brush(IslandColors.White(0.8)),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _labels.Add(label);
        stack.Children.Add(label);

        var tile = new Border
        {
            Child = stack,
            Height = 100,
            CornerRadius = new CornerRadius(10),
            Background = IslandColors.Brush(IslandColors.White(0.04)),
            BorderThickness = new Thickness(1.5),
            BorderBrush = Brushes.Transparent,
            Margin = new Thickness(0, 0, 8, 0),
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        tile.MouseLeftButtonUp += (_, args) =>
        {
            Select(style);
            StyleSelected?.Invoke(style);
            args.Handled = true;
        };
        return tile;
    }

    public void Select(ChartStyle selected)
    {
        var styles = Enum.GetValues<ChartStyle>();
        for (var i = 0; i < _tiles.Count; i++)
        {
            var isOn = styles[i] == selected;
            _tiles[i].BorderBrush = isOn ? IslandColors.Brush(SelectionBlue) : Brushes.Transparent;
            _tiles[i].Background = isOn
                ? IslandColors.Brush(SelectionBlue, 0.10)
                : IslandColors.Brush(IslandColors.White(0.04));
            // Selected caption goes selection-blue (macOS).
            _labels[i].Foreground = isOn
                ? IslandColors.Brush(SelectionBlue)
                : IslandColors.Brush(IslandColors.White(0.8));
        }
    }

    public static string StyleLabel(ChartStyle style) => Localization.L10n.Tr(style switch
    {
        ChartStyle.Stepped => "Stepped",
        ChartStyle.Bar => "Bar",
        ChartStyle.Ring => "Pie",
        ChartStyle.Numeric => "Numeric",
        _ => style.ToString(),
    });

    private static UIElement MakePreview(ChartStyle style)
    {
        var tint = IslandColors.Claude;
        switch (style)
        {
            case ChartStyle.Ring:
            {
                var host = new Grid { Width = 36, Height = 36, HorizontalAlignment = HorizontalAlignment.Center };
                host.Children.Add(new Ellipse
                {
                    Stroke = IslandColors.Brush(IslandColors.White(0.12)),
                    StrokeThickness = 4,
                });
                var arc = new System.Windows.Shapes.Path
                {
                    Stroke = IslandColors.Brush(tint),
                    StrokeThickness = 4,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    Data = ArcGeometry(36, 4, 120),
                };
                host.Children.Add(arc);
                return host;
            }
            case ChartStyle.Bar:
            {
                var host = new Grid { VerticalAlignment = VerticalAlignment.Center };
                host.Children.Add(new Border
                {
                    Height = 6,
                    CornerRadius = new CornerRadius(3),
                    Background = IslandColors.Brush(IslandColors.White(0.12)),
                });
                host.Children.Add(new Border
                {
                    Height = 6,
                    Width = 26,
                    CornerRadius = new CornerRadius(3),
                    Background = IslandColors.Brush(tint),
                    HorizontalAlignment = HorizontalAlignment.Left,
                });
                return host;
            }
            case ChartStyle.Stepped:
            {
                var row = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                for (var i = 0; i < 9; i++)
                {
                    row.Children.Add(new Rectangle
                    {
                        Width = 4,
                        Height = 18,
                        RadiusX = 1,
                        RadiusY = 1,
                        Margin = new Thickness(1.5, 0, 1.5, 0),
                        Fill = i < 4 ? IslandColors.Brush(tint) : IslandColors.Brush(IslandColors.White(0.12)),
                    });
                }
                return row;
            }
            case ChartStyle.Numeric:
            {
                var text = new TextBlock
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontFamily = IslandFonts.Mono,
                };
                text.Inlines.Add(new System.Windows.Documents.Run("35")
                {
                    FontSize = 20,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Brushes.White,
                });
                text.Inlines.Add(new System.Windows.Documents.Run(" %")
                {
                    FontSize = 11,
                    Foreground = IslandColors.Brush(IslandColors.White(0.5)),
                });
                return text;
            }
            default:
            {
                // Unreachable — every ChartStyle has its own case above.
                return new Grid();
            }
        }
    }

    private static Geometry ArcGeometry(double size, double stroke, double sweepDegrees)
    {
        var radius = (size - stroke) / 2;
        var center = new Point(size / 2, size / 2);
        var start = new Point(center.X, center.Y - radius);
        var angle = sweepDegrees * Math.PI / 180;
        var end = new Point(center.X + radius * Math.Sin(angle), center.Y - radius * Math.Cos(angle));
        var figure = new PathFigure { StartPoint = start, IsClosed = false };
        figure.Segments.Add(new ArcSegment(
            end, new Size(radius, radius), 0, sweepDegrees > 180, SweepDirection.Clockwise, true));
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }
}

/// Cost display picker: USD / VALUE / TOKENS / TREND preview tiles.
public sealed class CostStylePickerControl : Grid
{
    private static readonly Color SelectionBlue = Color.FromRgb(0x2E, 0x7C, 0xF6);
    private readonly List<Border> _tiles = new();

    public event Action<CostStyle>? StyleSelected;

    public CostStylePickerControl(CostStyle selected)
    {
        var styles = Enum.GetValues<CostStyle>();
        for (var i = 0; i < styles.Length; i++)
        {
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }
        for (var i = 0; i < styles.Length; i++)
        {
            var style = styles[i];
            var tile = MakeTile(style);
            SetColumn(tile, i);
            _tiles.Add(tile);
            Children.Add(tile);
        }
        Select(selected);
    }

    private readonly List<TextBlock> _labels = new();

    private Border MakeTile(CostStyle style)
    {
        var stack = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var preview = new Grid { Height = 40, Margin = new Thickness(0, 0, 0, 10) };
        preview.Children.Add(MakePreview(style));
        stack.Children.Add(preview);
        var label = new TextBlock
        {
            Text = ChipLabel(style),
            FontFamily = IslandFonts.Ui,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = IslandColors.Brush(IslandColors.White(0.65)),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _labels.Add(label);
        stack.Children.Add(label);
        var tile = new Border
        {
            Child = stack,
            Height = 100,
            CornerRadius = new CornerRadius(10),
            Background = IslandColors.Brush(IslandColors.White(0.04)),
            BorderThickness = new Thickness(1.5),
            BorderBrush = Brushes.Transparent,
            Margin = new Thickness(0, 0, 8, 0),
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        tile.MouseLeftButtonUp += (_, args) =>
        {
            Select(style);
            StyleSelected?.Invoke(style);
            args.Handled = true;
        };
        return tile;
    }

    public void Select(CostStyle selected)
    {
        var styles = Enum.GetValues<CostStyle>();
        for (var i = 0; i < _tiles.Count; i++)
        {
            var isOn = styles[i] == selected;
            _tiles[i].BorderBrush = isOn ? IslandColors.Brush(SelectionBlue) : Brushes.Transparent;
            _tiles[i].Background = isOn
                ? IslandColors.Brush(SelectionBlue, 0.10)
                : IslandColors.Brush(IslandColors.White(0.04));
            // The selected tile's caption goes selection-blue (macOS).
            _labels[i].Foreground = isOn
                ? IslandColors.Brush(SelectionBlue)
                : IslandColors.Brush(IslandColors.White(0.65));
        }
    }

    /// Drawn previews, not typed-out strings — "◞◠◞◠" rendered as tofu-ish
    /// glyph soup on Windows fonts, and the value tile reads as a chart on
    /// macOS, not a dollar string.
    private static UIElement MakePreview(CostStyle style)
    {
        var tint = IslandColors.Claude;
        switch (style)
        {
            case CostStyle.Dollar:
            {
                var text = new TextBlock
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontFamily = IslandFonts.Mono,
                };
                text.Inlines.Add(new System.Windows.Documents.Run("$ ")
                {
                    FontSize = 12,
                    Foreground = IslandColors.Brush(IslandColors.White(0.5)),
                });
                text.Inlines.Add(new System.Windows.Documents.Run("87")
                {
                    FontSize = 20,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = IslandColors.Brush(tint),
                });
                return text;
            }
            case CostStyle.Multi:
            {
                // The macOS tile sketches the value view: a dim dot next to
                // a tall tinted capsule.
                var row = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0, 0, 0, 6),
                };
                row.Children.Add(new Ellipse
                {
                    Width = 7,
                    Height = 7,
                    Fill = IslandColors.Brush(IslandColors.White(0.35)),
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0, 0, 5, 0),
                });
                row.Children.Add(new Border
                {
                    Width = 10,
                    Height = 26,
                    CornerRadius = new CornerRadius(5),
                    Background = IslandColors.Brush(tint),
                    VerticalAlignment = VerticalAlignment.Bottom,
                });
                return row;
            }
            case CostStyle.Tokens:
            {
                var text = new TextBlock
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontFamily = IslandFonts.Mono,
                };
                text.Inlines.Add(new System.Windows.Documents.Run("2.4")
                {
                    FontSize = 20,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Brushes.White,
                });
                text.Inlines.Add(new System.Windows.Documents.Run(" M")
                {
                    FontSize = 11,
                    Foreground = IslandColors.Brush(IslandColors.White(0.5)),
                });
                return text;
            }
            case CostStyle.Trend:
            default:
            {
                // A rising stroke, the way the macOS tile draws it.
                var line = new Polyline
                {
                    Stroke = IslandColors.Brush(tint),
                    StrokeThickness = 2.5,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    StrokeLineJoin = PenLineJoin.Round,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                line.Points.Add(new Point(0, 16));
                line.Points.Add(new Point(34, 2));
                return line;
            }
        }
    }

    private static string ChipLabel(CostStyle style) => Localization.L10n.Tr(style switch
    {
        CostStyle.Dollar => "USD",
        CostStyle.Multi => "Value",
        CostStyle.Tokens => "TOKEN",
        CostStyle.Trend => "Trend",
        _ => "USD",
    });
}

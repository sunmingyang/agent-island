using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using AgentIsland.UI.Charts;
using AgentIsland.UI.Theme;

namespace AgentIsland.UI.Report;

/// The two share cards, WPF renditions of the macOS SwiftUI originals.
/// Texture rules (the owner's bar: 质感第一): layered dark gradients, one
/// hairline top-light border, faint provider-colored auras, no flat chips.
/// Fixed 420x560 portrait; `rounded: false` builds the EXPORT version —
/// square corners and full-bleed, because social apps flatten transparency
/// to white and rounded transparent corners paste as ugly nicks.
public static class ReportCards
{
    public const double CardWidth = 420;
    public const double CardHeight = 560;

    private static readonly Color Ink = Color.FromRgb(0x13, 0x14, 0x17);      // 0.075/0.08/0.09
    private static readonly Color InkDeep = Color.FromRgb(0x07, 0x08, 0x0A);  // 0.028/0.03/0.038
    private static readonly Color MoneyGreen = Color.FromRgb(0x8C, 0xD9, 0x9E);
    private static readonly Color MilestoneGold = Color.FromRgb(0xFF, 0xC7, 0x6B);
    // The card's own voice is the brand teal; provider colors stay semantic
    // (split bar, dots) but no longer carry the headline.
    private static readonly Color BrandTeal = Color.FromRgb(0x20, 0xC0, 0xB0);
    private static readonly Color BrandTealLight = Color.FromRgb(0x7D, 0xF0, 0xE3);
    private static readonly Color LiveTeal = Color.FromRgb(0x3D, 0xD6, 0x8C);

    public static FrameworkElement Weekly(WeeklyReportData data, bool rounded = true)
    {
        var zh = ReportFormat.IsChinese;
        // Elastic gaps, the macOS Spacer(minLength:) behavior: sections keep
        // their minimum breathing room and the leftover height distributes
        // evenly, so the card reads composed instead of packed.
        var body = FlexColumn(
            (Header("WEEKLY", data.RangeText), 0),
            (Hero(Localization.L10n.Tr("tokens this week"), data.TotalTokens, data.TotalDollars, zh), 14),
            (ProviderSplit(data.ClaudeShare), 16),
            (WeekBars(data), 18),
            (ModelDonut(data, zh), 18));
        return Card(body, Footer(data.MilestoneText), rounded,
            auraA: (IslandColors.Claude, 0.13, new Point(0.12, 0.02), 340),
            auraB: (IslandColors.Codex, 0.11, new Point(0.95, 0.85), 380));
    }

    public static FrameworkElement Monthly(MonthlyReportData data, bool rounded = true)
    {
        var zh = ReportFormat.IsChinese;
        var body = FlexColumn(
            (Header("MONTHLY", data.MonthText), 0),
            (Hero(Localization.L10n.Tr("tokens this month"), data.TotalTokens, data.TotalDollars, zh), 14),
            (ProviderSplit(data.ClaudeShare), 16),
            (HeatBlock(data), 18));
        return Card(body, Footer(data.MilestoneText), rounded,
            auraA: (IslandColors.Codex, 0.14, new Point(0.9, 0.06), 360),
            auraB: (IslandColors.Claude, 0.09, new Point(0.06, 0.9), 340));
    }

    /// Sections interleaved with star-sized spacer rows carrying a minimum
    /// height — WPF's rendition of SwiftUI's Spacer(minLength:).
    private static Grid FlexColumn(params (UIElement Element, double MinGap)[] sections)
    {
        var grid = new Grid();
        var row = 0;
        foreach (var (element, minGap) in sections)
        {
            if (minGap > 0)
            {
                grid.RowDefinitions.Add(new RowDefinition
                {
                    Height = new GridLength(1, GridUnitType.Star),
                    MinHeight = minGap,
                });
                row++;
            }
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow((FrameworkElement)element, row);
            grid.Children.Add(element);
            row++;
        }
        return grid;
    }

    // MARK: - Scaffold

    private static FrameworkElement Card(
        UIElement body, UIElement footer, bool rounded,
        (Color Color, double Opacity, Point Center, double Radius) auraA,
        (Color Color, double Opacity, Point Center, double Radius) auraB)
    {
        var radius = rounded ? 26.0 : 0.0;
        var root = new Grid { Width = CardWidth, Height = CardHeight };

        root.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(radius),
            Background = new LinearGradientBrush(Ink, InkDeep, new Point(0, 0), new Point(1, 1)),
        });
        // Faint provider auras — enough to feel alive, never loud.
        root.Children.Add(Aura(auraA.Color, auraA.Opacity, auraA.Center, auraA.Radius, radius));
        root.Children.Add(Aura(auraB.Color, auraB.Opacity, auraB.Center, auraB.Radius, radius));
        root.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(radius),
            BorderThickness = new Thickness(1),
            BorderBrush = new LinearGradientBrush(
                IslandColors.White(0.16), IslandColors.White(0.02), new Point(0, 0), new Point(0, 1)),
        });

        // Body flows from the top; the footer pins to the bottom edge, so a
        // short model list never floats the brand strip upward.
        var content = new Grid { Margin = new Thickness(30) };
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(body, 0);
        Grid.SetRow(footer, 1);
        ((FrameworkElement)footer).Margin = new Thickness(0, 16, 0, 0);
        content.Children.Add(body);
        content.Children.Add(footer);
        root.Children.Add(content);

        root.Clip = new RectangleGeometry(new Rect(0, 0, CardWidth, CardHeight), radius, radius);
        System.Windows.Media.TextOptions.SetTextFormattingMode(root, TextFormattingMode.Ideal);
        return root;
    }

    private static UIElement Aura(Color color, double opacity, Point center, double radius, double cornerRadius)
    {
        return new Border
        {
            CornerRadius = new CornerRadius(cornerRadius),
            Background = new RadialGradientBrush
            {
                GradientOrigin = center,
                Center = center,
                RadiusX = radius / CardWidth,
                RadiusY = radius / CardHeight,
                MappingMode = BrushMappingMode.RelativeToBoundingBox,
                GradientStops = new GradientStopCollection
                {
                    new GradientStop(IslandColors.Alpha(color, opacity), 0),
                    new GradientStop(Colors.Transparent, 1),
                },
            },
        };
    }

    private static UIElement Header(string tag, string rangeText)
    {
        var row = new DockPanel { LastChildFill = false };
        var brand = new StackPanel { Orientation = Orientation.Horizontal };
        brand.Children.Add(new TextBlock
        {
            Text = "AGENT ISLAND",
            FontFamily = IslandFonts.Ui,
            FontSize = 11,
            FontWeight = FontWeights.ExtraBold,
            Foreground = IslandColors.Brush(IslandColors.White(0.85)),
        });
        brand.Children.Add(new TextBlock
        {
            Text = " " + tag,
            FontFamily = IslandFonts.Ui,
            FontSize = 11,
            FontWeight = FontWeights.ExtraBold,
            // Brand teal carries the wordmark on both cards (v2); provider
            // colors no longer take the headline.
            Foreground = new LinearGradientBrush(BrandTeal, BrandTealLight, 0),
        });
        DockPanel.SetDock(brand, Dock.Left);
        row.Children.Add(brand);
        var range = new TextBlock
        {
            Text = rangeText,
            FontFamily = IslandFonts.Mono,
            FontSize = 10.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = IslandColors.Brush(IslandColors.White(0.42)),
            VerticalAlignment = VerticalAlignment.Center,
        };
        DockPanel.SetDock(range, Dock.Right);
        row.Children.Add(range);
        return row;
    }

    private static UIElement Hero(string title, long totalTokens, double totalDollars, bool zh)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontFamily = IslandFonts.Ui,
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Foreground = IslandColors.Brush(IslandColors.White(0.55)),
        });
        var (value, unit) = ReportFormat.CompactParts(totalTokens, zh);
        var number = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
        var numberBrush = new LinearGradientBrush(
            Colors.White, IslandColors.White(0.72), new Point(0, 0), new Point(0, 1));
        number.Children.Add(new TextBlock
        {
            Text = value,
            FontFamily = IslandFonts.Ui,
            FontSize = 74,
            FontWeight = FontWeights.ExtraBold,
            Foreground = numberBrush,
            // WPF's default line box adds ~1/3 of the font size; the macOS
            // hero sits tight, so pin the line to the glyphs.
            LineHeight = 78,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
        });
        if (unit.Length > 0)
        {
            // 中文单位(亿/万)按惯例小一号挂在数字后;英文单位(B/M)同体量。
            number.Children.Add(new TextBlock
            {
                Text = unit,
                FontFamily = IslandFonts.Ui,
                FontSize = zh ? 38 : 74,
                FontWeight = FontWeights.ExtraBold,
                Foreground = numberBrush,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(2, 0, 0, zh ? 12 : 0),
            });
        }
        stack.Children.Add(number);
        if (totalDollars >= 1)
        {
            stack.Children.Add(new TextBlock
            {
                Text = Localization.L10n.TrFormat("≈ ${0} API value", ReportFormat.Money(totalDollars)),
                FontFamily = IslandFonts.Ui,
                FontSize = 13.5,
                FontWeight = FontWeights.ExtraBold,
                Foreground = IslandColors.Brush(LiveTeal),
                Margin = new Thickness(0, 4, 0, 0),
            });
        }
        return stack;
    }

    private static UIElement ProviderSplit(double claudeShare)
    {
        var stack = new StackPanel();
        var track = new Grid { Height = 7 };
        track.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(Math.Max(0.02, claudeShare), GridUnitType.Star),
        });
        track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2) });
        track.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(Math.Max(0.02, 1 - claudeShare), GridUnitType.Star),
        });
        var claudeBar = new Border { CornerRadius = new CornerRadius(3.5), Background = IslandColors.Brush(IslandColors.Claude) };
        var codexBar = new Border { CornerRadius = new CornerRadius(3.5), Background = IslandColors.Brush(IslandColors.Codex) };
        Grid.SetColumn(claudeBar, 0);
        Grid.SetColumn(codexBar, 2);
        track.Children.Add(claudeBar);
        track.Children.Add(codexBar);
        stack.Children.Add(track);

        var tags = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        tags.Children.Add(ProviderTag("Claude", claudeShare, IslandColors.Claude));
        tags.Children.Add(ProviderTag("Codex", 1 - claudeShare, IslandColors.Codex, leftMargin: 18));
        stack.Children.Add(tags);
        return stack;
    }

    /// Color dot, not a brand mark: exactly one logo per card (v2), and
    /// that one is Agent Island's in the footer.
    private static UIElement ProviderTag(string name, double pct, Color color, double leftMargin = 0)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(leftMargin, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        row.Children.Add(new Ellipse
        {
            Width = 7,
            Height = 7,
            Fill = IslandColors.Brush(color),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
        });
        row.Children.Add(new TextBlock
        {
            Text = name,
            FontFamily = IslandFonts.Ui,
            FontSize = 11.5,
            FontWeight = FontWeights.Bold,
            Foreground = IslandColors.Brush(IslandColors.White(0.78)),
            VerticalAlignment = VerticalAlignment.Center,
        });
        row.Children.Add(new TextBlock
        {
            Text = $" {Math.Round(pct * 100)}%",
            FontFamily = IslandFonts.Ui,
            FontSize = 11.5,
            FontWeight = FontWeights.ExtraBold,
            Foreground = IslandColors.Brush(color),
            VerticalAlignment = VerticalAlignment.Center,
        });
        return row;
    }

    // MARK: - Weekly sections

    private static UIElement WeekBars(WeeklyReportData data)
    {
        var peak = Math.Max(data.DailyTokens.Max(), 1);
        var grid = new Grid();
        for (var i = 0; i < 7; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }
        for (var i = 0; i < 7; i++)
        {
            var tokens = data.DailyTokens[i];
            var isPeak = tokens == peak && tokens > 0;
            var cell = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(5, 0, 5, 0),
            };
            cell.Children.Add(new Border
            {
                Height = Math.Max(5, 64.0 * tokens / peak),
                CornerRadius = new CornerRadius(3),
                Background = isPeak
                    ? new LinearGradientBrush(BrandTealLight, BrandTeal, 90)
                    : IslandColors.Brush(IslandColors.White(tokens > 0 ? 0.22 : 0.07)),
            });
            cell.Children.Add(new TextBlock
            {
                Text = i < data.DayLetters.Count ? data.DayLetters[i] : "",
                FontFamily = IslandFonts.Ui,
                FontSize = 9,
                FontWeight = FontWeights.Bold,
                Foreground = IslandColors.Brush(IslandColors.White(isPeak ? 0.75 : 0.32)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 6, 0, 0),
            });
            Grid.SetColumn(cell, i);
            grid.Children.Add(cell);
        }
        grid.Height = 84;
        return grid;
    }

    /// Donut + legend — every model carries all three numbers (tokens,
    /// dollars, share); segment 0 starts at 12 o'clock.
    private static UIElement ModelDonut(WeeklyReportData data, bool zh)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        const double size = 90;
        const double thickness = 13;
        var donut = new Grid { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Top };
        donut.Children.Add(new Ellipse
        {
            Stroke = IslandColors.Brush(IslandColors.White(0.06)),
            StrokeThickness = thickness,
            Margin = new Thickness(thickness / 2),
        });
        var cumulative = 0.0;
        foreach (var model in data.TopModels)
        {
            var from = cumulative;
            cumulative += model.Percent;
            // A hairline gap between segments keeps neighbors separable.
            var gap = model.Percent > 0.03 ? 0.006 : 0.0;
            var start = from + gap;
            var end = Math.Max(start, cumulative - gap);
            if (end - start <= 0.0005) continue;
            donut.Children.Add(DonutSegment(size, thickness, start, end, model.Color));
        }
        donut.Children.Add(new TextBlock
        {
            Text = $"TOP {data.TopModels.Count(m => !m.IsOthers)}",
            FontFamily = IslandFonts.Ui,
            FontSize = 10.5,
            FontWeight = FontWeights.ExtraBold,
            Foreground = IslandColors.Brush(IslandColors.White(0.5)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        });
        Grid.SetColumn(donut, 0);
        row.Children.Add(donut);

        var legend = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        foreach (var model in data.TopModels)
        {
            var line = new DockPanel { Margin = new Thickness(0, 3, 0, 3), LastChildFill = true };
            var dot = new Ellipse
            {
                Width = 7,
                Height = 7,
                Fill = IslandColors.Brush(model.Color),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 7, 0),
            };
            DockPanel.SetDock(dot, Dock.Left);
            line.Children.Add(dot);

            var numbers = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            numbers.Children.Add(new TextBlock
            {
                Text = ReportFormat.CompactString(model.Tokens, zh),
                FontFamily = IslandFonts.Ui,
                FontSize = 10,
                FontWeight = FontWeights.ExtraBold,
                Foreground = IslandColors.Brush(IslandColors.White(0.5)),
                Margin = new Thickness(6, 0, 0, 0),
            });
            numbers.Children.Add(new TextBlock
            {
                Text = $" ${ReportFormat.Money(model.Dollars)}",
                FontFamily = IslandFonts.Ui,
                FontSize = 10,
                FontWeight = FontWeights.ExtraBold,
                Foreground = IslandColors.Brush(IslandColors.Alpha(MoneyGreen, 0.9)),
            });
            numbers.Children.Add(new TextBlock
            {
                Text = $"{Math.Round(model.Percent * 100)}%",
                FontFamily = IslandFonts.Ui,
                FontSize = 10,
                FontWeight = FontWeights.ExtraBold,
                Foreground = IslandColors.Brush(IslandColors.White(0.88)),
                Width = 30,
                TextAlignment = TextAlignment.Right,
            });
            DockPanel.SetDock(numbers, Dock.Right);
            line.Children.Add(numbers);

            line.Children.Add(new TextBlock
            {
                Text = model.Name,
                FontFamily = IslandFonts.Ui,
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = IslandColors.Brush(IslandColors.White(model.IsOthers ? 0.5 : 0.85)),
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            });
            legend.Children.Add(line);
        }
        Grid.SetColumn(legend, 2);
        row.Children.Add(legend);
        return row;
    }

    private static System.Windows.Shapes.Path DonutSegment(
        double size, double thickness, double from, double to, Color color)
    {
        var center = size / 2;
        var radius = (size - thickness) / 2;
        // 0..1 → degrees from 12 o'clock, clockwise.
        var startAngle = from * 360 - 90;
        var endAngle = to * 360 - 90;
        Point PointAt(double deg)
        {
            var rad = deg * Math.PI / 180;
            return new Point(center + radius * Math.Cos(rad), center + radius * Math.Sin(rad));
        }
        var figure = new PathFigure { StartPoint = PointAt(startAngle), IsClosed = false };
        figure.Segments.Add(new ArcSegment(
            PointAt(endAngle),
            new Size(radius, radius),
            0,
            isLargeArc: endAngle - startAngle > 180,
            SweepDirection.Clockwise,
            isStroked: true));
        return new System.Windows.Shapes.Path
        {
            Data = new PathGeometry(new[] { figure }),
            Stroke = IslandColors.Brush(color),
            StrokeThickness = thickness,
        };
    }

    // MARK: - Monthly section

    // Brand-teal ramp (v2); the top level IS the brand teal.
    private static readonly Color[] HeatRamp =
    {
        IslandColors.White(0.06),
        Color.FromRgb(0x0B, 0x2F, 0x2A),
        Color.FromRgb(0x0E, 0x54, 0x4B),
        Color.FromRgb(0x13, 0x87, 0x7A),
        Color.FromRgb(0x20, 0xC0, 0xB0),
    };

    private static UIElement HeatBlock(MonthlyReportData data)
    {
        var stack = new StackPanel();

        var caption = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 0, 0, 10) };
        var weeksLabel = new TextBlock
        {
            Text = Localization.L10n.TrFormat("Past {0} weeks", data.WeeksCount),
            FontFamily = IslandFonts.Ui,
            FontSize = 11.5,
            FontWeight = FontWeights.Bold,
            Foreground = IslandColors.Brush(IslandColors.White(0.55)),
        };
        DockPanel.SetDock(weeksLabel, Dock.Left);
        caption.Children.Add(weeksLabel);
        if (data.StreakDays >= 2)
        {
            var streak = new TextBlock
            {
                Text = Localization.L10n.TrFormat("{0}-day streak 🔥", data.StreakDays),
                FontFamily = IslandFonts.Ui,
                FontSize = 12.5,
                FontWeight = FontWeights.ExtraBold,
                Foreground = IslandColors.Brush(Color.FromRgb(0xFF, 0x9E, 0x59)),
            };
            DockPanel.SetDock(streak, Dock.Right);
            caption.Children.Add(streak);
        }
        stack.Children.Add(caption);

        // 24 columns × 7 rows of 11px cells (2.5px gutters) = 322pt wide —
        // exactly the card's inner width, like the macOS grid.
        var gridRow = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var column in data.Heat)
        {
            var col = new StackPanel { Margin = new Thickness(0, 0, 2.5, 0) };
            foreach (var level in column)
            {
                var cell = new Border
                {
                    Width = 11,
                    Height = 11,
                    CornerRadius = new CornerRadius(2.5),
                    Margin = new Thickness(0, 0, 0, 2.5),
                    Background = level < 0
                        ? Brushes.Transparent
                        : IslandColors.Brush(HeatRamp[level]),
                };
                if (level == 4)
                {
                    cell.Effect = new System.Windows.Media.Effects.DropShadowEffect
                    {
                        ShadowDepth = 0,
                        BlurRadius = 9,
                        Color = HeatRamp[4],
                        Opacity = 0.5,
                    };
                }
                col.Children.Add(cell);
            }
            gridRow.Children.Add(col);
        }
        stack.Children.Add(gridRow);

        var legend = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 8, 0, 0) };
        var active = new TextBlock
        {
            Text = Localization.L10n.TrFormat("Active {0} days · peak {1}", data.ActiveDays, data.PeakText),
            FontFamily = IslandFonts.Ui,
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = IslandColors.Brush(IslandColors.White(0.42)),
        };
        DockPanel.SetDock(active, Dock.Left);
        legend.Children.Add(active);
        var ramp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        ramp.Children.Add(RampLabel(Localization.L10n.Tr("less")));
        foreach (var color in HeatRamp)
        {
            ramp.Children.Add(new Border
            {
                Width = 8,
                Height = 8,
                CornerRadius = new CornerRadius(2),
                Background = IslandColors.Brush(color),
                Margin = new Thickness(4, 0, 0, 0),
            });
        }
        var more = RampLabel(Localization.L10n.Tr("more"));
        more.Margin = new Thickness(4, 0, 0, 0);
        ramp.Children.Add(more);
        DockPanel.SetDock(ramp, Dock.Right);
        legend.Children.Add(ramp);
        stack.Children.Add(legend);
        return stack;
    }

    private static TextBlock RampLabel(string text) => new()
    {
        Text = text,
        FontFamily = IslandFonts.Ui,
        FontSize = 9,
        FontWeight = FontWeights.Bold,
        Foreground = IslandColors.Brush(IslandColors.White(0.35)),
        VerticalAlignment = VerticalAlignment.Center,
    };

    // MARK: - Footer (brand strip)

    /// Share-clean (v2): no QR, no repo URL — a centered app mark and the
    /// name are the card's entire sign-off.
    private static UIElement Footer(string? milestoneText)
    {
        var stack = new StackPanel();
        if (milestoneText is not null)
        {
            // The milestone caption — the "你已经很牛逼了" line, in the card.
            stack.Children.Add(new TextBlock
            {
                Text = milestoneText,
                FontFamily = IslandFonts.Ui,
                FontSize = 11,
                FontWeight = FontWeights.ExtraBold,
                Foreground = IslandColors.Brush(MilestoneGold),
                Margin = new Thickness(0, 0, 0, 12),
            });
        }
        stack.Children.Add(new Border
        {
            Height = 1,
            Background = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new GradientStop(IslandColors.White(0.0), 0),
                    new GradientStop(IslandColors.White(0.14), 0.5),
                    new GradientStop(IslandColors.White(0.0), 1),
                },
                new Point(0, 0), new Point(1, 0)),
        });

        var brand = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 12, 0, 0),
        };
        brand.Children.Add(new Image
        {
            Source = new BitmapImage(new Uri("pack://application:,,,/Assets/agentisland_logo.png")),
            Width = 30,
            Height = 30,
            VerticalAlignment = VerticalAlignment.Center,
        });
        brand.Children.Add(new TextBlock
        {
            Text = "Agent Island",
            FontFamily = IslandFonts.Ui,
            FontSize = 13,
            FontWeight = FontWeights.ExtraBold,
            Foreground = IslandColors.Brush(IslandColors.White(0.88)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
        });
        stack.Children.Add(brand);
        return stack;
    }
}


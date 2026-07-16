using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using AgentIsland.UI.Charts;
using AgentIsland.UI.Theme;

namespace AgentIsland.UI.Report;

/// The two share cards, v3 layout (locked 2026-07-16; character art lands
/// later in the reserved slot above the faceoff bar):
///   header  — app logo top-left on the wordmark line, date right
///   hero    — big number with the "≈ $X API value" line sharing its
///             baseline
///   faceoff — official provider logos at both ends, a two-color beam split
///             by share, a white spark at the meeting point, 144px of
///             head-room reserved for the character art
///   middle  — weekly: 7-day bars (peak in brand teal + its value) then a
///             TOP-3 model pie; monthly: a TOP-5 model pie (heatmap gone)
///   footer  — rank block, no plate, no divider: lifetime line + the
///             congratulations line in gold
/// Flat #0D0F13 base, no gradients. Numbers render tabular (no slashed
/// zeros). Fixed 420x560 portrait; `rounded: false` builds the EXPORT
/// version — square corners and full-bleed, because social apps flatten
/// transparency to white and rounded transparent corners paste as nicks.
public static class ReportCards
{
    public const double CardWidth = 420;
    public const double CardHeight = 560;

    private static readonly Color Base = Color.FromRgb(0x0D, 0x0F, 0x13);
    private static readonly Color BrandTeal = Color.FromRgb(0x20, 0xC0, 0xB0);
    private static readonly Color BrandTealLight = Color.FromRgb(0x7D, 0xF0, 0xE3);
    private static readonly Color LiveTeal = Color.FromRgb(0x3D, 0xD6, 0x8C);
    private static readonly Color RankGold = Color.FromRgb(0xE3, 0xB3, 0x4F);

    public static FrameworkElement Weekly(WeeklyReportData data, bool rounded = true)
    {
        var zh = ReportFormat.IsChinese;
        var body = FlexColumn(
            (Header("WEEKLY", data.RangeText), 0),
            (Hero(Localization.L10n.Tr("tokens this week"), data.TotalTokens, data.TotalDollars, zh), 6),
            (FaceoffStage(data.ClaudeShare, zh), 8),
            (WeekBars(data, zh), 10),
            (ModelTable(data.TopModels, zh), 10));
        return Card(body, RankFooter(data.Rank, zh), rounded);
    }

    public static FrameworkElement Monthly(MonthlyReportData data, bool rounded = true)
    {
        var zh = ReportFormat.IsChinese;
        var body = FlexColumn(
            (Header("MONTHLY", data.MonthText), 0),
            (Hero(Localization.L10n.Tr("tokens this month"), data.TotalTokens, data.TotalDollars, zh), 6),
            (FaceoffStage(data.ClaudeShare, zh), 10),
            (ModelTable(data.TopModels, zh), 12));
        return Card(body, RankFooter(data.Rank, zh), rounded);
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

    private static FrameworkElement Card(UIElement body, UIElement footer, bool rounded)
    {
        var radius = rounded ? 26.0 : 0.0;
        var root = new Grid { Width = CardWidth, Height = CardHeight };

        // v3: one flat base, no gradients, no auras. A uniform hairline
        // keeps the card from melting into dark chat backgrounds.
        root.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(radius),
            Background = IslandColors.Brush(Base),
            BorderThickness = new Thickness(1),
            BorderBrush = IslandColors.Brush(IslandColors.White(0.08)),
        });

        var content = new Grid { Margin = new Thickness(30, 22, 30, 18) };
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(body, 0);
        Grid.SetRow(footer, 1);
        ((FrameworkElement)footer).Margin = new Thickness(0, 10, 0, 0);
        content.Children.Add(body);
        content.Children.Add(footer);
        root.Children.Add(content);

        root.Clip = new RectangleGeometry(new Rect(0, 0, CardWidth, CardHeight), radius, radius);
        System.Windows.Media.TextOptions.SetTextFormattingMode(root, TextFormattingMode.Ideal);
        return root;
    }

    /// Numbers on the card read tabular — and Segoe UI's zero carries no
    /// slash, which the v3 spec calls out explicitly.
    private static TextBlock Numeric(TextBlock block)
    {
        Typography.SetNumeralAlignment(block, FontNumeralAlignment.Tabular);
        return block;
    }

    /// WPF has no letter-spacing; interleaved spaces fake the macOS wide
    /// wordmark tracking (the mac card spaces its letters visibly apart).
    private static string Track(string text) => string.Join(' ', text.ToCharArray());

    private static UIElement Header(string tag, string rangeText)
    {
        var row = new DockPanel { LastChildFill = false };
        var brand = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        // v3: the app mark moved up here from the footer — same line as the
        // wordmark, exactly one logo on the card (besides the faceoff pair).
        brand.Children.Add(new Image
        {
            Source = new BitmapImage(new Uri("pack://application:,,,/Assets/agentisland_logo.png")),
            Width = 20,
            Height = 20,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        });
        brand.Children.Add(new TextBlock
        {
            Text = Track("AGENT ISLAND"),
            FontFamily = IslandFonts.Ui,
            FontSize = 10.5,
            FontWeight = FontWeights.ExtraBold,
            Foreground = IslandColors.Brush(IslandColors.White(0.85)),
            VerticalAlignment = VerticalAlignment.Center,
        });
        brand.Children.Add(new TextBlock
        {
            Text = " " + Track(tag),
            FontFamily = IslandFonts.Ui,
            FontSize = 10.5,
            FontWeight = FontWeights.ExtraBold,
            Foreground = new LinearGradientBrush(BrandTeal, BrandTealLight, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        DockPanel.SetDock(brand, Dock.Left);
        row.Children.Add(brand);
        var range = Numeric(new TextBlock
        {
            Text = rangeText,
            FontFamily = IslandFonts.Ui,
            FontSize = 10.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = IslandColors.Brush(IslandColors.White(0.42)),
            VerticalAlignment = VerticalAlignment.Center,
        });
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
        // The big number and the "≈ $X" line share one baseline (v3): both
        // bottom-aligned in one row, the money line lifted to optically sit
        // on the digits' baseline rather than the line box's bottom.
        var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
        var numberBrush = new LinearGradientBrush(
            Colors.White, IslandColors.White(0.72), new Point(0, 0), new Point(0, 1));
        line.Children.Add(Numeric(new TextBlock
        {
            Text = value,
            FontFamily = IslandFonts.Ui,
            FontSize = 56,
            FontWeight = FontWeights.ExtraBold,
            Foreground = numberBrush,
            // WPF's default line box adds ~1/3 of the font size; pin the
            // line to the glyphs (P21 lesson).
            LineHeight = 60,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
        }));
        if (unit.Length > 0)
        {
            line.Children.Add(new TextBlock
            {
                Text = unit,
                FontFamily = IslandFonts.Ui,
                FontSize = zh ? 30 : 56,
                FontWeight = FontWeights.ExtraBold,
                Foreground = numberBrush,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(5, 0, 0, zh ? 7 : 0),
            });
        }
        if (totalDollars >= 1)
        {
            line.Children.Add(Numeric(new TextBlock
            {
                Text = Localization.L10n.TrFormat("≈ ${0} API value", ReportFormat.Money(totalDollars)),
                FontFamily = IslandFonts.Ui,
                FontSize = 13.5,
                FontWeight = FontWeights.ExtraBold,
                Foreground = IslandColors.Brush(LiveTeal),
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(12, 0, 0, 8),
            }));
        }
        // A wide month (17.12亿 + $2,043) can outgrow the card; scale the
        // whole line down rather than clip the money line's last glyph.
        stack.Children.Add(new Viewbox
        {
            Child = line,
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.DownOnly,
            MaxWidth = CardWidth - 60,
            HorizontalAlignment = HorizontalAlignment.Left,
        });
        return stack;
    }

    // MARK: - Faceoff stage

    /// The full duel block: character art on top (three poses picked by the
    /// share — Claude winning, a stand-off, Codex winning), the split beam
    /// they stand on, and the share legend under the bar's ends. Art PNGs
    /// ship from the macOS side as Assets/Report/faceoff-*.png; while a
    /// pose file is missing the stage keeps its height so the layout
    /// doesn't jump when the art lands.
    private static UIElement FaceoffStage(double claudeShare, bool zh)
    {
        var stack = new StackPanel();
        var pose = claudeShare >= 0.55 ? "claude-wins" : claudeShare <= 0.45 ? "codex-wins" : "tie";
        UIElement art;
        try
        {
            var bitmap = new BitmapImage(new Uri($"pack://application:,,,/Assets/Report/faceoff-{pose}.png"));
            art = new Image
            {
                Source = bitmap,
                Height = 112,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                // The characters STAND on the beam: the art's transparent
                // feet zone overlaps the bar (mac composition).
                Margin = new Thickness(0, 0, 0, -8),
            };
        }
        catch
        {
            art = new Border { Height = 104 }; // stage reserved until the art ships
        }
        stack.Children.Add(art);
        stack.Children.Add(FaceoffBar(claudeShare));

        // Share legend riding the bar's ends: ● Claude 67% … ● Codex 33%.
        var legend = new DockPanel { LastChildFill = false, Margin = new Thickness(2, 7, 2, 0) };
        var claudeSide = ShareTag("Claude", claudeShare, IslandColors.Claude);
        DockPanel.SetDock(claudeSide, Dock.Left);
        legend.Children.Add(claudeSide);
        var codexSide = ShareTag("Codex", 1 - claudeShare, IslandColors.Codex);
        DockPanel.SetDock(codexSide, Dock.Right);
        legend.Children.Add(codexSide);
        stack.Children.Add(legend);
        return stack;
    }

    private static UIElement ShareTag(string name, double share, Color color)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new Ellipse
        {
            Width = 6,
            Height = 6,
            Fill = IslandColors.Brush(color),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 5, 0),
        });
        row.Children.Add(new TextBlock
        {
            Text = name + " ",
            FontFamily = IslandFonts.Ui,
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = IslandColors.Brush(IslandColors.White(0.62)),
            VerticalAlignment = VerticalAlignment.Center,
        });
        row.Children.Add(Numeric(new TextBlock
        {
            Text = $"{Math.Round(share * 100)}%",
            FontFamily = IslandFonts.Ui,
            FontSize = 10,
            FontWeight = FontWeights.ExtraBold,
            Foreground = IslandColors.Brush(color),
            VerticalAlignment = VerticalAlignment.Center,
        }));
        return row;
    }

    /// Two official marks at the ends, two light beams meeting at the share
    /// split, a white spark at the collision.
    private static UIElement FaceoffBar(double claudeShare)
    {
        var row = new Grid { Height = 20 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var claudeMark = ProviderMark(BrandGeometry.ClaudePath, IslandColors.Claude);
        Grid.SetColumn(claudeMark, 0);
        row.Children.Add(claudeMark);

        var beams = new Grid { VerticalAlignment = VerticalAlignment.Center };
        var claudeStar = Math.Max(0.06, claudeShare);
        var codexStar = Math.Max(0.06, 1 - claudeShare);
        beams.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(claudeStar, GridUnitType.Star) });
        beams.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        beams.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(codexStar, GridUnitType.Star) });

        var claudeBeam = Beam(IslandColors.Claude, leftEnd: true);
        Grid.SetColumn(claudeBeam, 0);
        beams.Children.Add(claudeBeam);

        var codexBeam = Beam(IslandColors.Codex, leftEnd: false);
        Grid.SetColumn(codexBeam, 2);
        beams.Children.Add(codexBeam);

        // The spark: a white four-point star with a soft glow, riding the
        // meeting point of the beams.
        var spark = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M 7,0 L 8.8,5.2 L 14,7 L 8.8,8.8 L 7,14 L 5.2,8.8 L 0,7 L 5.2,5.2 Z"),
            Fill = Brushes.White,
            Width = 14,
            Height = 14,
            Stretch = Stretch.Fill,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(-4, 0, -4, 0),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                ShadowDepth = 0,
                BlurRadius = 14,
                Color = Colors.White,
                Opacity = 0.85,
            },
        };
        Grid.SetColumn(spark, 1);
        System.Windows.Controls.Panel.SetZIndex(spark, 1);
        beams.Children.Add(spark);

        Grid.SetColumn(beams, 2);
        row.Children.Add(beams);

        var codexMark = ProviderMark(BrandGeometry.OpenAiPath, IslandColors.Codex);
        Grid.SetColumn(codexMark, 4);
        row.Children.Add(codexMark);
        return row;
    }

    private static UIElement ProviderMark(string path, Color color) => new System.Windows.Shapes.Path
    {
        Data = Geometry.Parse("F1 " + path),
        Fill = IslandColors.Brush(color),
        Width = 14,
        Height = 14,
        Stretch = Stretch.Uniform,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static UIElement Beam(Color color, bool leftEnd)
    {
        return new Border
        {
            Height = 5,
            CornerRadius = leftEnd ? new CornerRadius(2.5, 0, 0, 2.5) : new CornerRadius(0, 2.5, 2.5, 0),
            Background = IslandColors.Brush(color),
            VerticalAlignment = VerticalAlignment.Center,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                ShadowDepth = 0,
                BlurRadius = 12,
                Color = color,
                Opacity = 0.55,
            },
        };
    }

    // MARK: - Weekly bars

    private static UIElement WeekBars(WeeklyReportData data, bool zh)
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
            if (isPeak)
            {
                // The peak day announces its number (v3), value and unit
                // spaced apart the way the mac card prints "11.2 亿".
                var (peakValue, peakUnit) = ReportFormat.CompactParts(tokens, zh);
                cell.Children.Add(Numeric(new TextBlock
                {
                    Text = peakUnit.Length > 0 ? $"{peakValue} {peakUnit}" : peakValue,
                    FontFamily = IslandFonts.Ui,
                    FontSize = 9.5,
                    FontWeight = FontWeights.ExtraBold,
                    Foreground = IslandColors.Brush(BrandTealLight),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 4),
                }));
            }
            cell.Children.Add(new Border
            {
                Height = Math.Max(5, 36.0 * tokens / peak),
                CornerRadius = new CornerRadius(3),
                Background = IslandColors.Brush(isPeak ? BrandTeal : IslandColors.White(tokens > 0 ? 0.22 : 0.07)),
            });
            cell.Children.Add(new TextBlock
            {
                Text = i < data.DayLetters.Count ? data.DayLetters[i] : "",
                FontFamily = IslandFonts.Ui,
                FontSize = 9,
                FontWeight = FontWeights.Bold,
                Foreground = IslandColors.Brush(IslandColors.White(isPeak ? 0.75 : 0.32)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 5, 0, 0),
            });
            Grid.SetColumn(cell, i);
            grid.Children.Add(cell);
        }
        return grid;
    }

    // MARK: - Model donut + table

    /// Donut with "TOP N" in the hole, and a four-column table beside it
    /// (the macOS v3 lock): header TOP 模型 | TOKEN | 费用 | 占比, rows with
    /// the color dot and DISPLAY model names (Fable 5, GPT-5.6-sol) — raw
    /// ids read like log spam on a share card.
    private static UIElement ModelTable(IReadOnlyList<ModelShare> models, bool zh)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        const double size = 80;
        const double thickness = 12;
        var donut = new Grid { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
        donut.Children.Add(new Ellipse
        {
            Stroke = IslandColors.Brush(IslandColors.White(0.06)),
            StrokeThickness = thickness,
            Margin = new Thickness(thickness / 2),
        });
        var cumulative = 0.0;
        var total = Math.Max(0.0001, models.Sum(m => m.Percent));
        foreach (var model in models)
        {
            var from = cumulative / total;
            cumulative += model.Percent;
            var to = cumulative / total;
            var gap = (to - from) > 0.03 ? 0.006 : 0.0;
            var start = from + gap;
            var end = Math.Max(start, to - gap);
            if (end - start <= 0.0005) continue;
            donut.Children.Add(DonutSegment(size, thickness, start, end, model.Color));
        }
        donut.Children.Add(new TextBlock
        {
            Text = $"TOP {models.Count(m => !m.IsOthers)}",
            FontFamily = IslandFonts.Ui,
            FontSize = 10,
            FontWeight = FontWeights.ExtraBold,
            Foreground = IslandColors.Brush(IslandColors.White(0.5)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        });
        Grid.SetColumn(donut, 0);
        row.Children.Add(donut);

        var table = new Grid { VerticalAlignment = VerticalAlignment.Center };
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(62) });
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });

        TextBlock HeaderCell(string key, TextAlignment align) => new()
        {
            Text = Localization.L10n.Tr(key),
            FontFamily = IslandFonts.Ui,
            FontSize = 8.5,
            FontWeight = FontWeights.Bold,
            Foreground = IslandColors.Brush(IslandColors.White(0.35)),
            TextAlignment = align,
            Margin = new Thickness(0, 0, 0, 3),
        };
        table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var headers = new[]
        {
            (HeaderCell("TOP Model", TextAlignment.Left), 0),
            (HeaderCell("Tokens", TextAlignment.Right), 1),
            (HeaderCell("Cost", TextAlignment.Right), 2),
            (HeaderCell("Share", TextAlignment.Right), 3),
        };
        foreach (var (cell, column) in headers)
        {
            Grid.SetRow(cell, 0);
            Grid.SetColumn(cell, column);
            table.Children.Add(cell);
        }

        var rowIndex = 1;
        foreach (var model in models)
        {
            table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var name = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 1.5, 0, 1.5),
                VerticalAlignment = VerticalAlignment.Center,
            };
            name.Children.Add(new Ellipse
            {
                Width = 7,
                Height = 7,
                Fill = IslandColors.Brush(model.Color),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0),
            });
            name.Children.Add(new TextBlock
            {
                Text = model.IsOthers ? model.Name : ReportFormat.DisplayModelName(model.Name),
                FontFamily = IslandFonts.Ui,
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
                Foreground = IslandColors.Brush(IslandColors.White(model.IsOthers ? 0.5 : 0.85)),
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            });
            Grid.SetRow(name, rowIndex);
            Grid.SetColumn(name, 0);
            table.Children.Add(name);

            var (tokenValue, tokenUnit) = ReportFormat.CompactParts(model.Tokens, zh);
            TextBlock Cell(string text, Color color, FontWeight weight)
            {
                var block = Numeric(new TextBlock
                {
                    Text = text,
                    FontFamily = IslandFonts.Ui,
                    FontSize = 10,
                    FontWeight = weight,
                    Foreground = IslandColors.Brush(color),
                    TextAlignment = TextAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 1.5, 0, 1.5),
                });
                return block;
            }
            var tokens = Cell(
                tokenUnit.Length > 0 ? $"{tokenValue} {tokenUnit}" : tokenValue,
                IslandColors.White(0.6), FontWeights.ExtraBold);
            Grid.SetRow(tokens, rowIndex);
            Grid.SetColumn(tokens, 1);
            table.Children.Add(tokens);

            var dollars = Cell($"${ReportFormat.Money(model.Dollars)}",
                IslandColors.Alpha(Color.FromRgb(0x8C, 0xD9, 0x9E), 0.9), FontWeights.ExtraBold);
            Grid.SetRow(dollars, rowIndex);
            Grid.SetColumn(dollars, 2);
            table.Children.Add(dollars);

            var share = Cell($"{Math.Round(model.Percent * 100)}%",
                IslandColors.White(0.88), FontWeights.ExtraBold);
            Grid.SetRow(share, rowIndex);
            Grid.SetColumn(share, 3);
            table.Children.Add(share);
            rowIndex++;
        }
        Grid.SetColumn(table, 2);
        row.Children.Add(table);
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

    // MARK: - Rank footer

    /// Two bare lines, no plate, no divider (v3): the lifetime total in
    /// white with the number stepped up, then the gold congratulations line
    /// with the tier name at display size.
    private static UIElement RankFooter(RankInfo? rank, bool zh)
    {
        // Both lines center on the card (macOS v3): the rank IS the card's
        // sign-off, not a side note.
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        if (rank is null) return stack;

        var lifetime = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        var (lifetimeValue, lifetimeUnit) = ReportFormat.CompactParts(rank.LifetimeTokens, zh);
        void AddPlain(string text)
        {
            lifetime.Children.Add(new TextBlock
            {
                Text = text,
                FontFamily = IslandFonts.Ui,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = IslandColors.Brush(IslandColors.White(0.82)),
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 2),
            });
        }
        AddPlain(zh ? "累计 " : "Lifetime ");
        lifetime.Children.Add(Numeric(new TextBlock
        {
            Text = lifetimeValue,
            FontFamily = IslandFonts.Ui,
            FontSize = 22,
            FontWeight = FontWeights.ExtraBold,
            Foreground = IslandColors.Brush(Colors.White),
            LineHeight = 24,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            VerticalAlignment = VerticalAlignment.Bottom,
        }));
        AddPlain(lifetimeUnit.Length > 0 ? " " + lifetimeUnit + " Token" : " Token");
        stack.Children.Add(lifetime);

        // "🏰 恭喜你已获得 岛主 段位" — tier name at 42px display weight,
        // everything else 27px, all in rank gold (v3 lock). The DownOnly
        // Viewbox is the overflow guard: a long tier name (群岛之王, or the
        // English "Legendary Navigator") scales the whole line to fit the
        // card instead of running off its edge.
        var congrats = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 0) };
        var gold = IslandColors.Brush(RankGold);
        const double bodySize = 27.0;
        const double nameSize = 42.0;
        void Add(string text, double size, FontWeight weight, double bottomPad = 0)
        {
            congrats.Children.Add(new TextBlock
            {
                Text = text,
                FontFamily = IslandFonts.Ui,
                FontSize = size,
                FontWeight = weight,
                Foreground = gold,
                VerticalAlignment = VerticalAlignment.Bottom,
                LineHeight = size + 4,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                Margin = new Thickness(0, 0, 0, bottomPad),
            });
        }
        if (zh)
        {
            Add(rank.TierEmoji + " 恭喜你已获得 ", bodySize, FontWeights.SemiBold, 3);
            Add(rank.TierName, nameSize, FontWeights.ExtraBold);
            Add(" 段位", bodySize, FontWeights.SemiBold, 3);
        }
        else
        {
            Add(rank.TierEmoji + " You've earned the ", bodySize, FontWeights.SemiBold, 3);
            Add(rank.TierName, nameSize, FontWeights.ExtraBold);
            Add(" rank", bodySize, FontWeights.SemiBold, 3);
        }
        stack.Children.Add(new Viewbox
        {
            Child = congrats,
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.DownOnly,
            MaxWidth = CardWidth - 60,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 5, 0, 0),
        });
        return stack;
    }
}

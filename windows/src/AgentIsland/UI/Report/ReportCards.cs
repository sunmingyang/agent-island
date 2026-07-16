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
            (Hero(Localization.L10n.Tr("tokens this week"), data.TotalTokens, data.TotalDollars, zh), 14),
            (FaceoffStage(data.ClaudeShare, zh), 12),
            (WeekBars(data, zh), 14),
            (ModelTable(data.TopModels, zh), 14));
        return Card(body, RankFooter(data.Rank, zh), rounded);
    }

    public static FrameworkElement Monthly(MonthlyReportData data, bool rounded = true)
    {
        var zh = ReportFormat.IsChinese;
        var body = FlexColumn(
            (Header("MONTHLY", data.MonthText), 0),
            (Hero(Localization.L10n.Tr("tokens this month"), data.TotalTokens, data.TotalDollars, zh), 14),
            (FaceoffStage(data.ClaudeShare, zh), 16),
            (ModelTable(data.TopModels, zh), 18));
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
            BorderBrush = IslandColors.Brush(IslandColors.White(0.06)),
        });

        var content = new Grid { Margin = new Thickness(28) };
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
        // v3: the APP ICON joins the wordmark. macOS drops in its plated
        // applicationIcon; the Windows .ico is the bare mark, so the plate
        // (rounded dark square + hairline) is drawn here around it.
        var plate = new Border
        {
            Width = 22,
            Height = 22,
            CornerRadius = new CornerRadius(5.5),
            Background = IslandColors.Brush(Color.FromRgb(0x15, 0x17, 0x1C)),
            BorderBrush = IslandColors.Brush(IslandColors.White(0.10)),
            BorderThickness = new Thickness(0.5),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 9, 0),
        };
        try
        {
            var logo = new Image
            {
                Source = new BitmapImage(new Uri("pack://application:,,,/Assets/agentisland_logo.png")),
                Width = 15,
                Height = 15,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            RenderOptions.SetBitmapScalingMode(logo, BitmapScalingMode.HighQuality);
            plate.Child = logo;
        }
        catch
        {
        }
        brand.Children.Add(plate);
        brand.Children.Add(new TextBlock
        {
            Text = Track("AGENT ISLAND"),
            FontFamily = IslandFonts.Ui,
            FontSize = 11,
            FontWeight = FontWeights.ExtraBold,
            Foreground = IslandColors.Brush(IslandColors.White(0.88)),
            VerticalAlignment = VerticalAlignment.Center,
        });
        brand.Children.Add(new TextBlock
        {
            Text = " " + Track(tag),
            FontFamily = IslandFonts.Ui,
            FontSize = 11,
            FontWeight = FontWeights.ExtraBold,
            Foreground = IslandColors.Brush(BrandTeal),
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
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Foreground = IslandColors.Brush(IslandColors.White(0.5)),
            Margin = new Thickness(0, 0, 0, 6),
        });
        var (value, unit) = ReportFormat.CompactParts(totalTokens, zh);
        // The big number and the "≈ $X" line share one baseline (v3); the
        // number is plain near-white — v3 dropped the vertical sheen.
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        var numberBrush = IslandColors.Brush(Color.FromRgb(0xF2, 0xF5, 0xF7));
        line.Children.Add(Numeric(new TextBlock
        {
            Text = value,
            FontFamily = IslandFonts.Ui,
            FontSize = 50,
            FontWeight = FontWeights.ExtraBold,
            Foreground = numberBrush,
            // WPF's default line box adds ~1/3 of the font size; pin the
            // line to the glyphs (P21 lesson).
            LineHeight = 54,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
        }));
        if (unit.Length > 0)
        {
            line.Children.Add(new TextBlock
            {
                Text = unit,
                FontFamily = IslandFonts.Ui,
                FontSize = zh ? 24 : 50,
                FontWeight = FontWeights.ExtraBold,
                Foreground = numberBrush,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(3, 0, 0, zh ? 6 : 0),
            });
        }
        if (totalDollars >= 1)
        {
            line.Children.Add(Numeric(new TextBlock
            {
                Text = Localization.L10n.TrFormat("≈ ${0} API value", ReportFormat.Money(totalDollars)),
                FontFamily = IslandFonts.Ui,
                FontSize = 12.5,
                FontWeight = FontWeights.ExtraBold,
                Foreground = IslandColors.Brush(BrandTeal),
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(12, 0, 0, 7),
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

    // MARK: - Faction duel (macOS ReportDuel, locked 2026-07-17)

    /// Official provider marks anchor the two ends of a split beam; the
    /// clash spark sits exactly at the usage-share split, and the chibi duel
    /// artwork stands over it. Three poses, picked from the share alone:
    /// Claude ≥52% wins (crowned, stomping), Codex ≥52% wins (pre-mirrored
    /// so the winner charges from his own end), 48–52% back-to-back draw.
    private const double DuelArtHeight = 82;
    private const double DuelMarkSide = 18;
    private const double DuelMarkGap = 10;
    private const double DuelBeamHeight = 6;

    private static UIElement FaceoffStage(double claudeShare, bool zh)
    {
        var contentWidth = CardWidth - 56; // 28pt card padding each side
        var beamX0 = DuelMarkSide + DuelMarkGap;
        var beamWidth = contentWidth - 2 * beamX0;
        var share = Math.Min(1, Math.Max(0, claudeShare));
        // The spark rides the TRUE split; only the artwork clamps inward so
        // a 90/10 blowout doesn't shove it off the card.
        var sparkX = beamX0 + beamWidth * Math.Min(0.97, Math.Max(0.03, share));
        var artX = beamX0 + beamWidth * Math.Min(0.74, Math.Max(0.26, share));
        var beamY = DuelArtHeight + 10;

        var canvas = new Canvas { Width = contentWidth, Height = DuelArtHeight + 20 };

        var pose = share >= 0.52 ? "duel-claude-wins" : share <= 0.48 ? "duel-codex-wins" : "duel-draw";
        try
        {
            var bitmap = new BitmapImage(new Uri($"pack://application:,,,/Assets/Report/{pose}.png"));
            var artWidth = DuelArtHeight * bitmap.PixelWidth / Math.Max(1, bitmap.PixelHeight);
            var art = new Image
            {
                Source = bitmap,
                Height = DuelArtHeight,
                Width = artWidth,
                Stretch = Stretch.Uniform,
            };
            RenderOptions.SetBitmapScalingMode(art, BitmapScalingMode.HighQuality);
            Canvas.SetLeft(art, artX - artWidth / 2);
            Canvas.SetTop(art, 1);
            canvas.Children.Add(art);
        }
        catch
        {
            // Art missing from the bundle: the stage keeps its height.
        }

        var claudeMark = ProviderMark(BrandGeometry.ClaudePath, IslandColors.Claude);
        Canvas.SetLeft(claudeMark, 0);
        Canvas.SetTop(claudeMark, beamY - DuelMarkSide / 2);
        canvas.Children.Add(claudeMark);

        var codexMark = ProviderMark(BrandGeometry.OpenAiPath, IslandColors.Codex);
        Canvas.SetLeft(codexMark, contentWidth - DuelMarkSide);
        Canvas.SetTop(codexMark, beamY - DuelMarkSide / 2);
        canvas.Children.Add(codexMark);

        // Two capsule beams meeting at the split, each brightening toward
        // its provider's end. No glow — the spark carries the light.
        var claudeBeamWidth = Math.Max(3, beamWidth * share - 0.75);
        var claudeBeam = new Border
        {
            Width = claudeBeamWidth,
            Height = DuelBeamHeight,
            CornerRadius = new CornerRadius(3),
            Background = new LinearGradientBrush(
                Color.FromRgb(0xE0, 0x8A, 0x63), IslandColors.Claude, 0),
        };
        Canvas.SetLeft(claudeBeam, beamX0);
        Canvas.SetTop(claudeBeam, beamY - DuelBeamHeight / 2);
        canvas.Children.Add(claudeBeam);

        var codexBeam = new Border
        {
            Width = Math.Max(3, beamWidth - claudeBeamWidth - 1.5),
            Height = DuelBeamHeight,
            CornerRadius = new CornerRadius(3),
            Background = new LinearGradientBrush(
                IslandColors.Codex, Color.FromRgb(0x7F, 0xBC, 0xF5), 0),
        };
        Canvas.SetLeft(codexBeam, beamX0 + claudeBeamWidth + 1.5);
        Canvas.SetTop(codexBeam, beamY - DuelBeamHeight / 2);
        canvas.Children.Add(codexBeam);

        canvas.Children.Add(ClashSpark(sparkX, beamY));

        var stack = new StackPanel();
        stack.Children.Add(canvas);

        // Share legend under the bar's ends: ● Claude 67% … ● Codex 33%.
        var legend = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 8, 0, 0) };
        var claudeSide = ShareTag("Claude", claudeShare, IslandColors.Claude);
        DockPanel.SetDock(claudeSide, Dock.Left);
        legend.Children.Add(claudeSide);
        var codexSide = ShareTag("Codex", 1 - claudeShare, IslandColors.Codex);
        DockPanel.SetDock(codexSide, Dock.Right);
        legend.Children.Add(codexSide);
        stack.Children.Add(legend);
        return stack;
    }

    /// White core + four-point star, warm shoulder to the Claude side and
    /// cool to the Codex side — the "swords meet here" moment.
    private static UIElement ClashSpark(double x, double y)
    {
        var spark = new Grid { Width = 20, Height = 20 };
        // Side lights first, under the star.
        var warm = new Ellipse
        {
            Width = 9,
            Height = 9,
            Fill = IslandColors.Brush(IslandColors.Alpha(IslandColors.Claude, 0.55)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(-10, 0, 0, 0),
        };
        var cool = new Ellipse
        {
            Width = 9,
            Height = 9,
            Fill = IslandColors.Brush(IslandColors.Alpha(IslandColors.Codex, 0.55)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
        };
        spark.Children.Add(warm);
        spark.Children.Add(cool);
        foreach (var angle in new[] { 14.0, 104.0 })
        {
            spark.Children.Add(new Border
            {
                Width = 1.6,
                Height = 17,
                CornerRadius = new CornerRadius(0.8),
                Background = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new RotateTransform(angle),
            });
        }
        spark.Children.Add(new Ellipse
        {
            Width = 7,
            Height = 7,
            Fill = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                ShadowDepth = 0,
                BlurRadius = 12,
                Color = Colors.White,
                Opacity = 0.95,
            },
        });
        Canvas.SetLeft(spark, x - 10);
        Canvas.SetTop(spark, y - 10);
        System.Windows.Controls.Panel.SetZIndex(spark, 2);
        return spark;
    }

    private static UIElement ShareTag(string name, double share, Color color)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
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
            Text = name + " ",
            FontFamily = IslandFonts.Ui,
            FontSize = 11.5,
            FontWeight = FontWeights.Bold,
            Foreground = IslandColors.Brush(IslandColors.White(0.8)),
            VerticalAlignment = VerticalAlignment.Center,
        });
        row.Children.Add(Numeric(new TextBlock
        {
            Text = $"{Math.Round(share * 100)}%",
            FontFamily = IslandFonts.Ui,
            FontSize = 11.5,
            FontWeight = FontWeights.ExtraBold,
            Foreground = IslandColors.Brush(color),
            VerticalAlignment = VerticalAlignment.Center,
        }));
        return row;
    }

    private static UIElement ProviderMark(string path, Color color) => new System.Windows.Shapes.Path
    {
        Data = Geometry.Parse("F1 " + path),
        Fill = IslandColors.Brush(color),
        Width = DuelMarkSide,
        Height = DuelMarkSide,
        Stretch = Stretch.Uniform,
    };

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
            // The value slot exists on every column (blank when not the
            // peak) so all seven bars share one floor line (macOS keeps the
            // row with a " " placeholder).
            cell.Children.Add(Numeric(new TextBlock
            {
                Text = isPeak ? ReportFormat.CompactString(tokens, zh) : " ",
                FontFamily = IslandFonts.Ui,
                FontSize = 9,
                FontWeight = FontWeights.ExtraBold,
                Foreground = IslandColors.Brush(BrandTeal),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 5),
            }));
            cell.Children.Add(new Border
            {
                Height = Math.Max(5, 58.0 * tokens / peak),
                CornerRadius = new CornerRadius(4),
                Background = IslandColors.Brush(isPeak ? BrandTeal : IslandColors.White(tokens > 0 ? 0.16 : 0.07)),
            });
            cell.Children.Add(new TextBlock
            {
                Text = i < data.DayLetters.Count ? data.DayLetters[i] : "",
                FontFamily = IslandFonts.Ui,
                FontSize = 9.5,
                FontWeight = FontWeights.Bold,
                Foreground = IslandColors.Brush(isPeak ? BrandTeal : IslandColors.White(0.32)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 5, 0, 0),
            });
            Grid.SetColumn(cell, i);
            grid.Children.Add(cell);
        }
        return grid;
    }

    // MARK: - Model donut + rows

    /// Donut with "TOP N" in the hole and plain rows beside it — no header
    /// row (macOS v3 final: the dot + display name + three numbers explain
    /// themselves). Segments sweep the TRUE share of dollars; the uncovered
    /// arc IS the long tail, so nothing is normalized and there is no
    /// "Others" row. Display names (Fable 5, GPT-5.6-sol) — raw ids read
    /// like log spam on a share card.
    private static UIElement ModelTable(IReadOnlyList<ModelShare> models, bool zh)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(88) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        const double size = 88;
        const double thickness = 13;
        var donut = new Grid { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
        donut.Children.Add(new Ellipse
        {
            Stroke = IslandColors.Brush(IslandColors.White(0.07)),
            StrokeThickness = thickness,
            Margin = new Thickness(thickness / 2),
        });
        var cumulative = 0.0;
        foreach (var model in models)
        {
            var from = cumulative;
            cumulative += model.Percent;
            var gap = model.Percent > 0.03 ? 0.006 : 0.0;
            var start = from + gap;
            var end = Math.Max(start, cumulative - gap);
            if (end - start <= 0.0005) continue;
            donut.Children.Add(DonutSegment(size, thickness, start, end, model.Color));
        }
        donut.Children.Add(new TextBlock
        {
            Text = Track($"TOP {models.Count}"),
            FontFamily = IslandFonts.Ui,
            FontSize = 10.5,
            FontWeight = FontWeights.ExtraBold,
            Foreground = IslandColors.Brush(IslandColors.White(0.5)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        });
        Grid.SetColumn(donut, 0);
        row.Children.Add(donut);

        var table = new Grid { VerticalAlignment = VerticalAlignment.Center };
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });

        var rowIndex = 0;
        foreach (var model in models)
        {
            table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var pad = rowIndex == 0 ? 0 : 4.5; // macOS row spacing 9

            var name = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, pad, 0, 4.5),
                VerticalAlignment = VerticalAlignment.Center,
            };
            name.Children.Add(new Ellipse
            {
                Width = 7,
                Height = 7,
                Fill = IslandColors.Brush(model.Color),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 7, 0),
            });
            name.Children.Add(new TextBlock
            {
                Text = ReportFormat.DisplayModelName(model.Name),
                FontFamily = IslandFonts.Ui,
                FontSize = 11.5,
                FontWeight = FontWeights.Bold,
                Foreground = IslandColors.Brush(IslandColors.White(0.85)),
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            });
            Grid.SetRow(name, rowIndex);
            Grid.SetColumn(name, 0);
            table.Children.Add(name);

            TextBlock Cell(string text, Color color)
            {
                return Numeric(new TextBlock
                {
                    Text = text,
                    FontFamily = IslandFonts.Ui,
                    FontSize = 10.5,
                    FontWeight = FontWeights.ExtraBold,
                    Foreground = IslandColors.Brush(color),
                    TextAlignment = TextAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(8, pad, 0, 4.5),
                });
            }
            var tokens = Cell(ReportFormat.CompactString(model.Tokens, zh), IslandColors.White(0.5));
            Grid.SetRow(tokens, rowIndex);
            Grid.SetColumn(tokens, 1);
            table.Children.Add(tokens);

            var dollars = Cell($"${ReportFormat.Money(model.Dollars)}",
                IslandColors.Alpha(Color.FromRgb(0x8C, 0xD9, 0x9E), 0.9));
            Grid.SetRow(dollars, rowIndex);
            Grid.SetColumn(dollars, 2);
            table.Children.Add(dollars);

            var share = Cell($"{Math.Round(model.Percent * 100)}%", IslandColors.White(0.88));
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

    /// The v3 rank block — bottom of both cards, centered. Line one: the
    /// lifetime total as one quiet 12pt caption. Line two: the
    /// congratulation in achievement gold with the rank name oversized
    /// (23px zh / 19px en heavy). No plate, no divider (owner: 金框很丑).
    private static UIElement RankFooter(RankInfo? rank, bool zh)
    {
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        if (rank is null) return stack;

        stack.Children.Add(Numeric(new TextBlock
        {
            Text = Localization.L10n.TrFormat(
                "lifetime {0} tokens", ReportFormat.CompactString(rank.LifetimeTokens, zh)),
            FontFamily = IslandFonts.Ui,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = IslandColors.Brush(IslandColors.White(0.62)),
            HorizontalAlignment = HorizontalAlignment.Center,
        }));

        var congrats = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 7, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        var gold = IslandColors.Brush(RankGold);
        var nameSize = zh ? 23.0 : 19.0;
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
                LineHeight = size + 3,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                Margin = new Thickness(0, 0, 0, bottomPad),
            });
        }
        Add(rank.TierEmoji + " " + Localization.L10n.Tr("Congrats, you've reached "), 14, FontWeights.ExtraBold, 2);
        Add(rank.TierName, nameSize, FontWeights.Black);
        Add(Localization.L10n.Tr(" rank"), 14, FontWeights.ExtraBold, 2);
        // Overflow guard for long tier names ("Legendary Navigator").
        stack.Children.Add(new Viewbox
        {
            Child = congrats,
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.DownOnly,
            MaxWidth = CardWidth - 60,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        return stack;
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using AgentIsland.Cost;
using AgentIsland.UI.Charts;
using AgentIsland.UI.Theme;

namespace AgentIsland.UI;

/// Cost page: per provider, today's spend as the hero number, a cumulative
/// sparkline for the day, and month + token context lines. The hero swaps
/// between USD / TOKENS / TREND per the cost style preference.
public sealed class CostPage : Border
{
    private readonly CostBlock _claude = new(IslandColors.Claude);
    private readonly CostBlock _codex = new(IslandColors.Codex);

    public CostPage()
    {
        Padding = new Thickness(22, 12, 22, 6);
        var grid = new Grid();
        Child = grid;
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        Grid.SetColumn(_claude, 0);
        grid.Children.Add(_claude);
        var hairline = new Border
        {
            Width = 1,
            Margin = new Thickness(0, 8, 0, 8),
            Background = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new GradientStop(Colors.Transparent, 0),
                    new GradientStop(IslandColors.White(0.06), 0.5),
                    new GradientStop(Colors.Transparent, 1),
                },
                new Point(0, 0),
                new Point(0, 1)),
        };
        Grid.SetColumn(hairline, 1);
        grid.Children.Add(hairline);
        Grid.SetColumn(_codex, 2);
        grid.Children.Add(_codex);

        CostStore.Shared.PropertyChanged += (_, _) => Dispatcher.BeginInvoke(Update);
        CostStylePreferenceStore.Shared.PropertyChanged += (_, _) => Dispatcher.BeginInvoke(Update);
        Update();
    }

    private void Update()
    {
        _claude.Update(CostStore.Shared.Claude);
        _codex.Update(CostStore.Shared.Codex);
    }
}

/// One provider's cost column.
public sealed class CostBlock : StackPanel
{
    private readonly TextBlock _hero;
    private readonly TextBlock _heroCaption;
    private readonly Sparkline _sparkline;
    private readonly TextBlock _monthLine;
    private readonly TextBlock _tokenLine;

    public CostBlock(Color tint)
    {
        Orientation = Orientation.Vertical;
        Margin = new Thickness(12, 0, 12, 0);

        var heroRow = new StackPanel { Orientation = Orientation.Horizontal };
        _hero = new TextBlock
        {
            FontFamily = IslandFonts.Mono,
            FontSize = 34,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
        };
        _heroCaption = new TextBlock
        {
            FontFamily = IslandFonts.Ui,
            FontSize = 11,
            FontWeight = FontWeights.Medium,
            Foreground = IslandColors.Brush(IslandColors.White(0.55)),
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(8, 0, 0, 6),
            Text = Localization.L10n.Tr("today"),
        };
        heroRow.Children.Add(_hero);
        heroRow.Children.Add(_heroCaption);
        Children.Add(heroRow);

        _sparkline = new Sparkline(tint)
        {
            Height = 30,
            Margin = new Thickness(0, 8, 0, 8),
        };
        Children.Add(_sparkline);

        _monthLine = new TextBlock
        {
            FontFamily = IslandFonts.Ui,
            FontSize = 11,
            FontWeight = FontWeights.Medium,
            Foreground = IslandColors.Brush(IslandColors.White(0.55)),
        };
        Children.Add(_monthLine);

        _tokenLine = new TextBlock
        {
            FontFamily = IslandFonts.Mono,
            FontSize = 10,
            Foreground = IslandColors.Brush(IslandColors.White(0.4)),
            Margin = new Thickness(0, 3, 0, 0),
        };
        Children.Add(_tokenLine);
    }

    public void Update(ProviderCostSummary summary)
    {
        switch (CostStylePreferenceStore.Shared.Style)
        {
            case CostStyle.Tokens:
                _hero.Text = Core.Formatting.CompactTokens(summary.TodayTokens);
                _heroCaption.Text = Localization.L10n.Tr("tokens today");
                _sparkline.SetSeries(summary.TodayCumulativeDollars);
                _monthLine.Text = Localization.L10n.TrFormat(
                    "{0} tokens this month", Core.Formatting.CompactTokens(summary.MonthTokens));
                _tokenLine.Text = Localization.L10n.TrFormat(
                    "{0} billable", Core.Formatting.CompactTokens(summary.TodayBillableTokens));
                break;
            case CostStyle.Trend:
                _hero.Text = Core.Formatting.Money(summary.MonthDollars);
                _heroCaption.Text = Localization.L10n.Tr("this month");
                _sparkline.SetSeries(summary.MonthCumulativeDollars);
                _monthLine.Text = Localization.L10n.TrFormat(
                    "{0} today", Core.Formatting.Money(summary.TodayDollars));
                _tokenLine.Text = Localization.L10n.TrFormat(
                    "{0} tokens · {1} billable",
                    Core.Formatting.CompactTokens(summary.MonthTokens),
                    Core.Formatting.CompactTokens(summary.MonthBillableTokens));
                break;
            case CostStyle.Multi:
                _hero.Text = Core.Formatting.Money(summary.TodayDollars);
                _heroCaption.Text = Localization.L10n.Tr("today");
                _sparkline.SetSeries(summary.TodayCumulativeDollars);
                _monthLine.Text = Localization.L10n.TrFormat(
                    "{0} this month · {1} tokens",
                    Core.Formatting.Money(summary.MonthDollars),
                    Core.Formatting.CompactTokens(summary.MonthTokens));
                _tokenLine.Text = Localization.L10n.TrFormat(
                    "{0} tokens · {1} billable",
                    Core.Formatting.CompactTokens(summary.TodayTokens),
                    Core.Formatting.CompactTokens(summary.TodayBillableTokens));
                break;
            case CostStyle.Dollar:
            default:
                _hero.Text = Core.Formatting.Money(summary.TodayDollars);
                _heroCaption.Text = Localization.L10n.Tr("today");
                _sparkline.SetSeries(summary.TodayCumulativeDollars);
                _monthLine.Text = Localization.L10n.TrFormat(
                    "{0} this month", Core.Formatting.Money(summary.MonthDollars));
                _tokenLine.Text = Localization.L10n.TrFormat(
                    "{0} tokens · {1} billable",
                    Core.Formatting.CompactTokens(summary.TodayTokens),
                    Core.Formatting.CompactTokens(summary.TodayBillableTokens));
                break;
        }
    }
}

/// Minimal cumulative-cost sparkline: soft area fill under a tinted line.
public sealed class Sparkline : Grid
{
    private readonly Polyline _line;
    private readonly Polygon _fill;
    private IReadOnlyList<double> _series = Array.Empty<double>();

    public Sparkline(Color tint)
    {
        _fill = new Polygon
        {
            Fill = new LinearGradientBrush(
                Color.FromArgb(60, tint.R, tint.G, tint.B),
                Color.FromArgb(0, tint.R, tint.G, tint.B),
                90),
        };
        _line = new Polyline
        {
            Stroke = IslandColors.Brush(tint),
            StrokeThickness = 1.5,
            StrokeLineJoin = PenLineJoin.Round,
        };
        Children.Add(_fill);
        Children.Add(_line);
        SizeChanged += (_, _) => Render();
    }

    public void SetSeries(IReadOnlyList<double> series)
    {
        _series = series;
        Render();
    }

    private void Render()
    {
        _line.Points.Clear();
        _fill.Points.Clear();
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 0 || height <= 0 || _series.Count < 2) return;
        var max = Math.Max(_series.Max(), 0.0001);
        for (var i = 0; i < _series.Count; i++)
        {
            var x = width * i / (_series.Count - 1);
            var y = height - height * (_series[i] / max) * 0.92 - 1;
            _line.Points.Add(new Point(x, y));
            _fill.Points.Add(new Point(x, y));
        }
        _fill.Points.Add(new Point(width, height));
        _fill.Points.Add(new Point(0, height));
    }
}

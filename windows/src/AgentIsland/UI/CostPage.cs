using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
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

        void ApplyVisibility()
        {
            var visibility = Model.ProviderVisibilityStore.Shared;
            _claude.Visibility = visibility.ClaudeVisible ? Visibility.Visible : Visibility.Collapsed;
            _codex.Visibility = visibility.CodexVisible ? Visibility.Visible : Visibility.Collapsed;
            hairline.Visibility = visibility.ClaudeVisible && visibility.CodexVisible
                ? Visibility.Visible
                : Visibility.Collapsed;
            // A lone provider takes the full width, like the macOS single
            // centered column.
            Grid.SetColumn(_claude, 0);
            Grid.SetColumnSpan(_claude, visibility.CodexVisible ? 1 : 3);
            Grid.SetColumn(_codex, visibility.ClaudeVisible ? 2 : 0);
            Grid.SetColumnSpan(_codex, visibility.ClaudeVisible ? 1 : 3);
        }

        // PagedContent recreates this page on visibility/screen changes;
        // detach on Unloaded or each dead instance stays pinned by the
        // singleton stores and keeps running Update forever.
        System.ComponentModel.PropertyChangedEventHandler onUpdate =
            (_, _) => Dispatcher.BeginInvoke(Update);
        System.ComponentModel.PropertyChangedEventHandler onVisibility =
            (_, _) => Dispatcher.BeginInvoke(ApplyVisibility);
        CostStore.Shared.PropertyChanged += onUpdate;
        CostStylePreferenceStore.Shared.PropertyChanged += onUpdate;
        Model.ProviderVisibilityStore.Shared.PropertyChanged += onVisibility;
        Unloaded += (_, _) =>
        {
            CostStore.Shared.PropertyChanged -= onUpdate;
            CostStylePreferenceStore.Shared.PropertyChanged -= onUpdate;
            Model.ProviderVisibilityStore.Shared.PropertyChanged -= onVisibility;
        };
        ApplyVisibility();
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
    private readonly CountUp _countUp;

    public CostBlock(Color tint)
    {
        Orientation = Orientation.Vertical;
        Margin = new Thickness(12, 0, 12, 0);

        var heroRow = new StackPanel { Orientation = Orientation.Horizontal };
        // Brand-tinted hero with a spend-scaled glow and count-up — the
        // macOS CostBlock centerpiece.
        _hero = new TextBlock
        {
            FontFamily = IslandFonts.Mono,
            FontSize = 34,
            FontWeight = FontWeights.SemiBold,
            Foreground = IslandColors.Brush(tint),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                ShadowDepth = 0,
                BlurRadius = 10,
                Color = tint,
                Opacity = 0,
            },
        };
        _countUp = new CountUp(_hero);
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
                _countUp.Animate(summary.TodayTokens, "tokens",
                    v => Core.Formatting.CompactTokens((long)Math.Round(v)));
                ApplyGlow(summary.TodayDollars);
                _heroCaption.Text = Localization.L10n.Tr("tokens today");
                _sparkline.SetSeries(summary.TodayCumulativeDollars);
                _monthLine.Text = Localization.L10n.TrFormat(
                    "{0} tokens this month", Core.Formatting.CompactTokens(summary.MonthTokens));
                _tokenLine.Text = Localization.L10n.TrFormat(
                    "{0} billable", Core.Formatting.CompactTokens(summary.TodayBillableTokens));
                break;
            case CostStyle.Trend:
                _countUp.Animate(summary.MonthDollars, "money", Core.Formatting.Money);
                ApplyGlow(summary.MonthDollars);
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
                _countUp.Animate(summary.TodayDollars, "money", Core.Formatting.Money);
                ApplyGlow(summary.TodayDollars);
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
                _countUp.Animate(summary.TodayDollars, "money", Core.Formatting.Money);
                ApplyGlow(summary.TodayDollars);
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

    /// Glow scales logarithmically with spend, capped at 0.85 (macOS rule):
    /// a $0 day is calm, a heavy day radiates.
    private void ApplyGlow(double dollars)
    {
        var glow = (System.Windows.Media.Effects.DropShadowEffect)_hero.Effect;
        glow.Opacity = dollars <= 0
            ? 0
            : Math.Min(0.85, 0.30 + 0.16 * Math.Log10(1 + dollars));
    }
}

/// 0.65s cubic-ease-out numeric count-up — the macOS CountUpDollar
/// behavior, driven by a 60Hz dispatcher timer only while animating.
/// The `unit` guards against counting across incompatible scales: switching
/// the cost style from tokens (millions) to dollars must snap, not tick a
/// 2,000,000 token count rendered as "$2,000,000" down to "$15".
internal sealed class CountUp
{
    private const double Seconds = 0.65;
    private readonly TextBlock _target;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private Func<double, string> _format = _ => "";
    private string _unit = "";
    private double _from;
    private double _to;
    private double _current;
    private bool _seeded;
    private DateTime _start;

    public CountUp(TextBlock target)
    {
        _target = target;
        _timer.Tick += (_, _) => Tick();
    }

    public void Animate(double to, string unit, Func<double, string> format)
    {
        _format = format;
        // First appearance: reveal by counting from zero.
        if (!_seeded)
        {
            _seeded = true;
            _unit = unit;
            Run(0, to);
            return;
        }
        // Unit change (tokens <-> dollars): snap, never count across scales.
        if (unit != _unit)
        {
            _unit = unit;
            _timer.Stop();
            _to = _current = to;
            _target.Text = format(to);
            return;
        }
        // Same value already shown: nothing to animate.
        if (Math.Abs(to - _to) < 0.000001)
        {
            if (!_timer.IsEnabled) _target.Text = format(to);
            return;
        }
        // Same unit, new value: continue from what's on screen right now.
        Run(_current, to);
    }

    private void Run(double from, double to)
    {
        _from = from;
        _to = to;
        _current = from;
        _start = DateTime.UtcNow;
        _target.Text = _format(from);
        _timer.Start();
    }

    private void Tick()
    {
        var x = Math.Clamp((DateTime.UtcNow - _start).TotalSeconds / Seconds, 0, 1);
        var eased = 1 - Math.Pow(1 - x, 3);
        _current = _from + (_to - _from) * eased;
        _target.Text = _format(_current);
        if (x >= 1) _timer.Stop();
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
                IslandColors.Alpha(tint, 0.45),
                IslandColors.Alpha(tint, 0),
                90),
        };
        _line = new Polyline
        {
            Stroke = IslandColors.Brush(tint),
            StrokeThickness = 1.5,
            StrokeLineJoin = PenLineJoin.Round,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                ShadowDepth = 0,
                BlurRadius = 3,
                Color = tint,
                Opacity = 0.7,
            },
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

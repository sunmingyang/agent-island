using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using AgentIsland.Cost;
using AgentIsland.UI.Charts;
using AgentIsland.UI.Theme;

namespace AgentIsland.UI;

/// Year-to-date contribution grid: one cell per day, intensity from token
/// volume, hue blended between the two providers' shares. Clicking a day
/// opens the detail strip below the grid.
public sealed class OverviewPage : Border
{
    private const int Rows = 7;
    private readonly Canvas _gridCanvas = new();
    private readonly TextBlock _detail;
    private Dictionary<DateTime, (long ClaudeTokens, double ClaudeDollars, long CodexTokens, double CodexDollars)> _days = new();

    public OverviewPage()
    {
        Padding = new Thickness(22, 12, 22, 6);
        var grid = new Grid();
        Child = grid;
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        Grid.SetRow(_gridCanvas, 0);
        grid.Children.Add(_gridCanvas);

        _detail = new TextBlock
        {
            FontFamily = IslandFonts.Ui,
            FontSize = 11,
            FontWeight = FontWeights.Medium,
            Foreground = IslandColors.Brush(IslandColors.White(0.55)),
            Margin = new Thickness(2, 8, 0, 0),
            Text = Localization.L10n.Tr("Click a day for details"),
        };
        Grid.SetRow(_detail, 1);
        grid.Children.Add(_detail);

        CostStore.Shared.PropertyChanged += (_, _) => Dispatcher.BeginInvoke(Rebuild);
        SizeChanged += (_, _) => Rebuild();
    }

    private void Rebuild()
    {
        _days = MergeHistory(CostStore.Shared.Claude.DailyHistory, CostStore.Shared.Codex.DailyHistory);
        _gridCanvas.Children.Clear();
        var width = _gridCanvas.ActualWidth;
        var height = _gridCanvas.ActualHeight;
        if (width <= 0 || height <= 0) return;

        var now = DateTime.Today;
        var jan1 = new DateTime(now.Year, 1, 1);
        // Grid columns are ISO-ish weeks: column 0 starts on the Monday on or
        // before Jan 1.
        var start = jan1.AddDays(-(((int)jan1.DayOfWeek + 6) % 7));
        var totalDays = (now - start).Days + 1;
        var columns = (totalDays + Rows - 1) / Rows;
        var pitchX = width / Math.Max(columns, 1);
        var pitchY = height / Rows;
        var cell = Math.Max(3, Math.Min(pitchX, pitchY) - 3);

        var maxTokens = Math.Max(1, _days.Count == 0 ? 1 : _days.Values.Max(v => v.ClaudeTokens + v.CodexTokens));
        for (var day = start; day <= now; day = day.AddDays(1))
        {
            var index = (day - start).Days;
            var column = index / Rows;
            var row = index % Rows;
            _days.TryGetValue(day, out var value);
            var total = value.ClaudeTokens + value.CodexTokens;
            var rect = new Rectangle
            {
                Width = cell,
                Height = cell,
                RadiusX = 2,
                RadiusY = 2,
                Fill = CellBrush(day < jan1 ? -1 : total, maxTokens, value),
                Cursor = System.Windows.Input.Cursors.Hand,
            };
            var captured = day;
            rect.MouseLeftButtonUp += (_, args) =>
            {
                ShowDetail(captured);
                args.Handled = true;
            };
            Canvas.SetLeft(rect, column * pitchX);
            Canvas.SetTop(rect, row * pitchY);
            _gridCanvas.Children.Add(rect);
        }
    }

    private static Brush CellBrush(
        long totalTokens,
        long maxTokens,
        (long ClaudeTokens, double ClaudeDollars, long CodexTokens, double CodexDollars) value)
    {
        if (totalTokens < 0) return Brushes.Transparent;
        if (totalTokens == 0) return IslandColors.Brush(IslandColors.White(0.06));
        // Log-scaled intensity so light days stay visible next to monsters.
        var intensity = Math.Clamp(Math.Log10(1 + totalTokens) / Math.Log10(1 + maxTokens), 0.15, 1.0);
        var codexShare = value.CodexTokens / (double)totalTokens;
        var color = Lerp(IslandColors.Claude, IslandColors.Codex, codexShare);
        return IslandColors.Brush(color, intensity);
    }

    private static Color Lerp(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t),
        (byte)(a.G + (b.G - a.G) * t),
        (byte)(a.B + (b.B - a.B) * t));

    private void ShowDetail(DateTime day)
    {
        _days.TryGetValue(day, out var value);
        var date = day.ToString(Localization.L10n.IsChinese ? "M月d日" : "MMM d");
        _detail.Text = $"{date} · Claude {Core.Formatting.CompactTokens(value.ClaudeTokens)} " +
            $"({Core.Formatting.Money(value.ClaudeDollars)}) · " +
            $"Codex {Core.Formatting.CompactTokens(value.CodexTokens)} " +
            $"({Core.Formatting.Money(value.CodexDollars)})";
    }

    private static Dictionary<DateTime, (long, double, long, double)> MergeHistory(
        IReadOnlyList<DailyTokenBucket> claude,
        IReadOnlyList<DailyTokenBucket> codex)
    {
        var merged = new Dictionary<DateTime, (long ClaudeTokens, double ClaudeDollars, long CodexTokens, double CodexDollars)>();
        foreach (var bucket in claude)
        {
            var key = bucket.DayStart.Date;
            merged.TryGetValue(key, out var entry);
            merged[key] = (entry.ClaudeTokens + bucket.Tokens, entry.ClaudeDollars + bucket.Dollars,
                entry.CodexTokens, entry.CodexDollars);
        }
        foreach (var bucket in codex)
        {
            var key = bucket.DayStart.Date;
            merged.TryGetValue(key, out var entry);
            merged[key] = (entry.ClaudeTokens, entry.ClaudeDollars,
                entry.CodexTokens + bucket.Tokens, entry.CodexDollars + bucket.Dollars);
        }
        return merged;
    }
}

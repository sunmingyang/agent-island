using System.Globalization;
using System.Windows.Media;
using AgentIsland.Cost;
using AgentIsland.UI.Theme;

namespace AgentIsland.UI.Report;

/// The shareable weekly report — assembled from LOCAL data only (CostStore's
/// log scan). Users copy or save it as a PNG and post it themselves; nothing
/// is ever uploaded, which is what lets this exist at all under the
/// no-telemetry promise. Direct port of the macOS WeeklyReportData.
public sealed record WeeklyReportData(
    string RangeText,
    long TotalTokens,
    double TotalDollars,
    double ClaudeShare,
    IReadOnlyList<long> DailyTokens,   // oldest → today, exactly 7
    IReadOnlyList<string> DayLetters,
    IReadOnlyList<WeeklyReportData.ModelShare> TopModels,
    string? MilestoneText)
{
    public sealed record ModelShare(
        string Name, long Tokens, double Dollars, double Percent, Color Color, bool IsOthers = false);

    // Ranked categorical palette — provider-shaded hues made neighboring
    // donut segments indistinguishable; the provider still reads from the
    // model name itself.
    private static readonly Color[] Palette =
    {
        Color.FromRgb(90, 168, 240),   // blue
        Color.FromRgb(204, 120, 92),   // coral
        Color.FromRgb(232, 194, 104),  // amber
        Color.FromRgb(91, 200, 175),   // teal
        Color.FromRgb(167, 139, 250),  // violet
    };

    public static WeeklyReportData Current()
    {
        var cost = CostStore.Shared;
        var today = DateTime.Today;
        var days = Enumerable.Range(0, 7).Reverse().Select(offset => today.AddDays(-offset)).ToArray();

        static long BucketTotal(IReadOnlyList<DailyTokenBucket> buckets, DateTime day) =>
            buckets.FirstOrDefault(b => b.DayStart.Date == day)?.Tokens ?? 0;
        var claudeDaily = days.Select(d => BucketTotal(cost.Claude.DailyHistory, d)).ToArray();
        var codexDaily = days.Select(d => BucketTotal(cost.Codex.DailyHistory, d)).ToArray();
        var daily = claudeDaily.Zip(codexDaily, (a, b) => a + b).ToArray();

        var claudeWeek = claudeDaily.Sum();
        var total = claudeWeek + codexDaily.Sum();

        // Rank models by DOLLARS, not billable tokens: the card's story is
        // "what my week was worth", and token-ranking buried expensive
        // models. Wire tokens (cache included) — same accounting as the hero
        // total, so the rows visibly sum toward the headline number.
        var allSpend = cost.Claude.WeeklyModels.Concat(cost.Codex.WeeklyModels).ToList();
        var dollars = allSpend.Sum(m => m.Dollars);
        var dollarUniverse = Math.Max(0.01, dollars);
        var ranked = allSpend
            .Select(m => (m.Model, m.Tokens, m.Dollars, Percent: m.Dollars / dollarUniverse))
            .OrderByDescending(m => m.Percent)
            .ToList();

        // Top 5 by spend; everything past the fold folds into one dim
        // "Others" row. ≥0.5% keeps noise rows off.
        var models = ranked
            .Where(m => m.Percent >= 0.005)
            .Take(5)
            .Select((m, i) => new ModelShare(m.Model, m.Tokens, m.Dollars, m.Percent,
                Palette[Math.Min(i, Palette.Length - 1)]))
            .ToList();
        var shown = models.Select(m => m.Name).ToHashSet(StringComparer.Ordinal);
        var rest = ranked.Where(m => !shown.Contains(m.Model)).ToList();
        if (rest.Count > 0)
        {
            var restPercent = rest.Sum(m => m.Percent);
            if (restPercent >= 0.005)
            {
                models.Add(new ModelShare(
                    Localization.L10n.Tr("Others"),
                    rest.Sum(m => m.Tokens), rest.Sum(m => m.Dollars), restPercent,
                    Color.FromRgb(0x6B, 0x6B, 0x6B), IsOthers: true));
            }
        }

        // The card follows the app language — a card destined for WeChat
        // groups must read Chinese when the UI is Chinese.
        var zh = ReportFormat.IsChinese;
        var range = zh
            ? $"{days[0]:M月d日} – {today:M月d日}"
            : $"{days[0].ToString("MMM d", CultureInfo.InvariantCulture)} – {today.ToString("MMM d", CultureInfo.InvariantCulture)}";

        var zhDays = new[] { "日", "一", "二", "三", "四", "五", "六" };
        var letters = days
            .Select(d => zh
                ? zhDays[(int)d.DayOfWeek]
                : d.ToString("ddd", CultureInfo.InvariantCulture)[..1])
            .ToArray();

        return new WeeklyReportData(
            range,
            total,
            dollars,
            total > 0 ? (double)claudeWeek / total : 0,
            daily,
            letters,
            models,
            ReportFormat.MilestoneText(cost));
    }
}

/// The monthly share card — the weekly card's big sibling: this month's
/// totals up top, and a 24-week activity heatmap (the streak brag) as the
/// centerpiece. Same no-upload rules. Port of the macOS MonthlyReportData.
public sealed record MonthlyReportData(
    string MonthText,
    long TotalTokens,
    double TotalDollars,
    double ClaudeShare,
    IReadOnlyList<IReadOnlyList<int>> Heat,   // [week][row Mon→Sun]; -1 outside, 0..4 intensity
    int WeeksCount,
    int StreakDays,
    int ActiveDays,
    string PeakText,
    string? MilestoneText)
{
    public static MonthlyReportData Current()
    {
        var cost = CostStore.Shared;
        var today = DateTime.Today;
        var zh = ReportFormat.IsChinese;

        // Merge both providers' daily buckets into one map.
        var daily = new Dictionary<DateTime, long>();
        foreach (var bucket in cost.Claude.DailyHistory.Concat(cost.Codex.DailyHistory))
        {
            var day = bucket.DayStart.Date;
            daily[day] = daily.GetValueOrDefault(day) + bucket.Tokens;
        }
        var firstDataDay = daily.Count > 0 ? daily.Keys.Min() : today;

        var totalTokens = cost.Claude.MonthTokens + cost.Codex.MonthTokens;
        var totalDollars = cost.Claude.MonthDollars + cost.Codex.MonthDollars;
        var claudeShare = totalTokens > 0 ? (double)cost.Claude.MonthTokens / totalTokens : 0;

        // 24 heat weeks ending with the current (Monday-started) week.
        const int weeks = 24;
        var sinceMonday = ((int)today.DayOfWeek + 6) % 7;
        var gridStart = today.AddDays(-sinceMonday).AddDays(-7 * (weeks - 1));

        // Quartile levels over the window's nonzero days (GitHub-style) — a
        // linear scale would flatline everything next to a 3.5B peak day.
        var windowValues = new List<long>();
        for (var day = gridStart; day <= today; day = day.AddDays(1))
        {
            var v = daily.GetValueOrDefault(day);
            if (v > 0) windowValues.Add(v);
        }
        var sorted = windowValues.OrderBy(v => v).ToArray();
        long Quartile(double q) =>
            sorted.Length == 0 ? 1 : sorted[Math.Min(sorted.Length - 1, (int)(q * sorted.Length))];
        var q1 = Quartile(0.25);
        var q2 = Quartile(0.5);
        var q3 = Quartile(0.75);
        int Level(long v) => v <= 0 ? 0 : v < q1 ? 1 : v < q2 ? 2 : v < q3 ? 3 : 4;

        var heat = new List<IReadOnlyList<int>>(weeks);
        for (var col = 0; col < weeks; col++)
        {
            var column = new int[7];
            for (var row = 0; row < 7; row++)
            {
                var d = gridStart.AddDays(col * 7 + row);
                column[row] = d > today || d < firstDataDay ? -1 : Level(daily.GetValueOrDefault(d));
            }
            heat.Add(column);
        }

        // Current streak: consecutive active days ending today (or yesterday
        // when today hasn't logged anything yet — don't zero the brag at 9am).
        var streak = 0;
        var cursor = daily.GetValueOrDefault(today) > 0 ? today : today.AddDays(-1);
        while (daily.GetValueOrDefault(cursor) > 0)
        {
            streak++;
            cursor = cursor.AddDays(-1);
        }

        var peak = windowValues.Count > 0 ? windowValues.Max() : 0;

        return new MonthlyReportData(
            zh ? $"{today:yyyy年M月}" : today.ToString("MMMM yyyy", CultureInfo.InvariantCulture),
            totalTokens,
            totalDollars,
            claudeShare,
            heat,
            weeks,
            streak,
            windowValues.Count,
            ReportFormat.CompactString(peak, zh),
            ReportFormat.MilestoneText(cost));
    }
}

/// Shared number/caption formatting for both cards.
public static class ReportFormat
{
    public static bool IsChinese => Localization.L10n.IsChinese;

    /// "🏆 岛主段位 · 累计 227亿 Token" — recognition rides the card itself;
    /// null until the first tier (100M lifetime) is crossed. Lifetime is the
    /// full local history CostStore holds, matching the macOS accounting.
    public static string? MilestoneText(CostStore cost)
    {
        var lifetime = cost.Claude.DailyHistory.Concat(cost.Codex.DailyHistory).Sum(b => b.Tokens);
        if (Model.MilestoneLadder.TokenTier(lifetime) is not { } tier) return null;
        return tier.Emoji + " " + Localization.L10n.TrFormat(
            "{0} rank · lifetime {1} tokens",
            Localization.L10n.Tr(tier.NameKey),
            CompactString(lifetime, IsChinese));
    }

    public static string CompactString(long n, bool zh)
    {
        var (value, unit) = CompactParts(n, zh);
        return value + unit;
    }

    /// (value, unit). Chinese counts in 亿/万 — the way the number is
    /// actually said — English in B/M/K.
    public static (string Value, string Unit) CompactParts(long n, bool zh)
    {
        double v = n;
        if (zh)
        {
            if (v >= 100_000_000) return (Trim(v / 100_000_000), "亿");
            if (v >= 10_000) return (Trim(v / 10_000), "万");
            return (n.ToString(CultureInfo.InvariantCulture), "");
        }
        return v switch
        {
            >= 1_000_000_000 => (Trim(v / 1_000_000_000), "B"),
            >= 1_000_000 => (Trim(v / 1_000_000), "M"),
            >= 1_000 => (Trim(v / 1_000), "K"),
            _ => (n.ToString(CultureInfo.InvariantCulture), ""),
        };
    }

    private static string Trim(double v)
    {
        // No trailing zeros — "99.5亿", never "99.50亿".
        var s = v >= 100
            ? v.ToString("F0", CultureInfo.InvariantCulture)
            : v.ToString("F2", CultureInfo.InvariantCulture);
        if (s.Contains('.'))
        {
            s = s.TrimEnd('0').TrimEnd('.');
        }
        return s;
    }

    public static string Money(double v) => Math.Round(v).ToString("N0", CultureInfo.InvariantCulture);
}

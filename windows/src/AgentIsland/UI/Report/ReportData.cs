using System.Globalization;
using System.Windows.Media;
using AgentIsland.Cost;
using AgentIsland.UI.Theme;

namespace AgentIsland.UI.Report;

/// One pie slice / legend row of the model breakdown.
public sealed record ModelShare(
    string Name, long Tokens, double Dollars, double Percent, Color Color, bool IsOthers = false);

/// The shareable weekly report — assembled from LOCAL data only (CostStore's
/// log scan). Users copy or save it as a PNG and post it themselves; nothing
/// is ever uploaded, which is what lets this exist at all under the
/// no-telemetry promise.
public sealed record WeeklyReportData(
    string RangeText,
    long TotalTokens,
    double TotalDollars,
    double ClaudeShare,
    IReadOnlyList<long> DailyTokens,   // oldest → today, exactly 7
    IReadOnlyList<string> DayLetters,
    IReadOnlyList<ModelShare> TopModels,
    RankInfo? Rank)
{
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
        var dollars = cost.Claude.WeeklyModels.Concat(cost.Codex.WeeklyModels).Sum(m => m.Dollars);

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
            // v3 weekly pie: TOP 3 + Others.
            ReportFormat.BuildTopModels(
                cost.Claude.WeeklyModels.Concat(cost.Codex.WeeklyModels), top: 3),
            ReportFormat.Rank(cost));
    }
}

/// The monthly share card — v3 drops the heatmap; the month's model mix
/// (TOP 5 pie) is the centerpiece under the faceoff bar.
public sealed record MonthlyReportData(
    string MonthText,
    long TotalTokens,
    double TotalDollars,
    double ClaudeShare,
    IReadOnlyList<ModelShare> TopModels,
    RankInfo? Rank)
{
    public static MonthlyReportData Current()
    {
        var cost = CostStore.Shared;
        var today = DateTime.Today;
        var zh = ReportFormat.IsChinese;

        var totalTokens = cost.Claude.MonthTokens + cost.Codex.MonthTokens;
        var totalDollars = cost.Claude.MonthDollars + cost.Codex.MonthDollars;
        var claudeShare = totalTokens > 0 ? (double)cost.Claude.MonthTokens / totalTokens : 0;

        return new MonthlyReportData(
            zh ? $"{today:yyyy年M月}" : today.ToString("MMMM yyyy", CultureInfo.InvariantCulture),
            totalTokens,
            totalDollars,
            claudeShare,
            ReportFormat.BuildTopModels(
                cost.Claude.MonthModels.Concat(cost.Codex.MonthModels), top: 5),
            ReportFormat.Rank(cost));
    }
}

/// The rank footer's raw parts: lifetime total plus the earned tier.
public sealed record RankInfo(long LifetimeTokens, string TierEmoji, string TierName);

/// Shared number/caption formatting for both cards.
public static class ReportFormat
{
    public static bool IsChinese => Localization.L10n.IsChinese;

    // Ranked categorical palette — provider-shaded hues made neighboring
    // pie slices indistinguishable; the provider still reads from the
    // model name itself.
    private static readonly Color[] Palette =
    {
        Color.FromRgb(90, 168, 240),   // blue
        Color.FromRgb(204, 120, 92),   // coral
        Color.FromRgb(232, 194, 104),  // amber
        Color.FromRgb(91, 200, 175),   // teal
        Color.FromRgb(167, 139, 250),  // violet
    };

    /// Rank models by DOLLARS: the card's story is "what my period was
    /// worth", and token-ranking buried expensive models. Wire tokens
    /// (cache included) — same accounting as the hero total, so the rows
    /// visibly sum toward the headline number. Top N + a dim "Others".
    public static IReadOnlyList<ModelShare> BuildTopModels(IEnumerable<ModelSpend> spend, int top)
    {
        var all = spend.ToList();
        var dollarUniverse = Math.Max(0.01, all.Sum(m => m.Dollars));
        var ranked = all
            .Select(m => (m.Model, m.Tokens, m.Dollars, Percent: m.Dollars / dollarUniverse))
            .OrderByDescending(m => m.Percent)
            .ToList();

        var models = ranked
            .Where(m => m.Percent >= 0.005)
            .Take(top)
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
        return models;
    }

    /// Lifetime total + earned tier for the rank footer; null until the
    /// first tier (100M lifetime) is crossed. Lifetime is the full local
    /// history CostStore holds, matching the macOS accounting.
    public static RankInfo? Rank(CostStore cost)
    {
        var lifetime = cost.Claude.DailyHistory.Concat(cost.Codex.DailyHistory).Sum(b => b.Tokens);
        if (Model.MilestoneLadder.TokenTier(lifetime) is not { } tier) return null;
        return new RankInfo(lifetime, tier.Emoji, Localization.L10n.Tr(tier.NameKey));
    }

    public static string CompactString(long n, bool zh)
    {
        var (value, unit) = CompactParts(n, zh);
        return value + unit;
    }

    /// Share-card display names: "claude-fable-5" reads like log spam next
    /// to a gold rank line — the cards print "Fable 5", "Opus 4.8",
    /// "GPT-5.6-sol" (macOS v3 lock). Unknown shapes pass through.
    public static string DisplayModelName(string raw)
    {
        if (raw.StartsWith("gpt-", StringComparison.OrdinalIgnoreCase))
        {
            return "GPT-" + raw[4..];
        }
        if (raw.StartsWith("claude-", StringComparison.OrdinalIgnoreCase))
        {
            var parts = raw[7..].Split('-');
            if (parts.Length >= 2 && parts.Skip(1).All(p => p.All(char.IsDigit)))
            {
                var family = char.ToUpperInvariant(parts[0][0]) + parts[0][1..];
                return family + " " + string.Join('.', parts.Skip(1));
            }
        }
        return raw;
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

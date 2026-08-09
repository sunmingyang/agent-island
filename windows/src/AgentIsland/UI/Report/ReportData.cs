using System.Globalization;
using System.Windows.Media;
using AgentIsland.Cost;

namespace AgentIsland.UI.Report;

/// One pie slice / legend row of the model breakdown. Carries its owning
/// provider so the card can price it ("$X") or show "—" (Cursor/Gemini), the
/// same per-row honesty split macOS ModelShare keeps.
public sealed record ModelShare(
    string Name, long Tokens, double Dollars, double Percent, Color Color,
    Model.DisplayProvider Provider, bool IsOthers = false);

/// One provider's token roll-up for a report period — the atom the top-2
/// duel and the cross-provider totals are built from. Replaces the hardcoded
/// Claude/Codex share so a Grok-only or Cursor+Gemini period still renders a
/// meaningful card.
public sealed record ProviderPeriodSlice(Model.DisplayProvider Provider, long Tokens);

/// The shareable weekly report — assembled from LOCAL data only (CostStore's
/// log scan). Users copy or save it as a PNG and post it themselves; nothing
/// is ever uploaded, which is what lets this exist at all under the
/// no-telemetry promise.
public sealed record WeeklyReportData(
    string RangeText,
    long TotalTokens,
    double TotalDollars,
    IReadOnlyList<ProviderPeriodSlice> Providers,   // token desc, only providers that ran
    IReadOnlyList<long> DailyTokens,   // oldest → today, exactly 7, summed across all providers
    IReadOnlyList<string> DayLetters,
    IReadOnlyList<ModelShare> TopModels)
{
    public static WeeklyReportData Current()
    {
        var cost = CostStore.Shared;
        var today = DateTime.Today;
        var days = Enumerable.Range(0, 7).Reverse().Select(offset => today.AddDays(-offset)).ToArray();

        static long BucketTotal(IReadOnlyList<DailyTokenBucket> buckets, DateTime day) =>
            buckets.FirstOrDefault(b => b.DayStart.Date == day)?.Tokens ?? 0;

        long WeekTokens(Model.DisplayProvider provider) =>
            days.Sum(d => BucketTotal(cost.Summary(provider).DailyHistory, d));

        // Every provider that ran, ranked by tokens — the top-2 face off, the
        // rest still contribute to the totals and the model ring.
        var providers = Model.DisplayProviders.All
            .Select(provider => new ProviderPeriodSlice(provider, WeekTokens(provider)))
            .Where(slice => slice.Tokens > 0)
            .OrderByDescending(slice => slice.Tokens)
            .ToList();

        var daily = days
            .Select(d => Model.DisplayProviders.All.Sum(p => BucketTotal(cost.Summary(p).DailyHistory, d)))
            .ToArray();
        var total = providers.Sum(slice => slice.Tokens);
        // Tokens-but-no-dollars providers (Cursor) carry $0 rows, so they add
        // nothing to the dollar total on their own; the "≈" hero copy keeps it
        // an estimate.
        var dollars = Model.DisplayProviders.All.Sum(p => cost.Summary(p).WeeklyModels.Sum(m => m.Dollars));

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
            providers,
            daily,
            letters,
            // TOP 3 across every provider that ran (owner call,
            // 2026-08-09: 只要写前三的模型就够了).
            ReportFormat.BuildTopModels(ReportFormat.ProviderModels(cost, s => s.WeeklyModels), top: 3));
    }
}

/// The monthly share card — v3 drops the heatmap; the month's model mix
/// (TOP 5 pie) is the centerpiece under the faceoff bar.
public sealed record MonthlyReportData(
    string MonthText,
    long TotalTokens,
    double TotalDollars,
    IReadOnlyList<ProviderPeriodSlice> Providers,   // token desc, only providers that ran
    IReadOnlyList<ModelShare> TopModels)
{
    public static MonthlyReportData Current()
    {
        var cost = CostStore.Shared;
        var today = DateTime.Today;
        var zh = ReportFormat.IsChinese;

        var providers = Model.DisplayProviders.All
            .Select(provider => new ProviderPeriodSlice(provider, cost.Summary(provider).MonthTokens))
            .Where(slice => slice.Tokens > 0)
            .OrderByDescending(slice => slice.Tokens)
            .ToList();
        var totalTokens = providers.Sum(slice => slice.Tokens);
        // Cursor's MonthDollars is 0 (no model → no price), so it contributes
        // tokens above but nothing to the dollar total.
        var totalDollars = Model.DisplayProviders.All.Sum(p => cost.Summary(p).MonthDollars);

        return new MonthlyReportData(
            zh ? $"{today:yyyy年M月}" : today.ToString("MMMM yyyy", CultureInfo.InvariantCulture),
            totalTokens,
            totalDollars,
            providers,
            ReportFormat.BuildTopModels(ReportFormat.ProviderModels(cost, s => s.MonthModels), top: 3));
    }
}

/// Shared number/caption formatting for both cards.
public static class ReportFormat
{
    public static bool IsChinese => Localization.L10n.IsChinese;

    /// Every model that ran in the period, tagged with the provider it came
    /// from, across ALL five providers. Providers with no local ledger
    /// (Gemini) yield nothing; tokens-only Cursor yields rows the token-ranked
    /// BuildTopModels keeps and the card prices as "—".
    public static IEnumerable<(Model.DisplayProvider Provider, ModelSpend Spend)> ProviderModels(
        CostStore cost, Func<ProviderCostSummary, IReadOnlyList<ModelSpend>> select)
    {
        foreach (var provider in Model.DisplayProviders.All)
        {
            foreach (var spend in select(cost.Summary(provider)))
            {
                yield return (provider, spend);
            }
        }
    }

    /// Rank models by TOKEN share — the one metric every provider defines
    /// (macOS 2026-08-08 owner call). Dollar-ranking (the old two-provider
    /// behavior) filtered a tokens-only period — Cursor ships tokens with no
    /// price, Gemini nothing — down to an empty donut, and left the donut's
    /// proportions on a different axis than the token hero. Wire tokens (cache
    /// included), same accounting as the hero total, so the ring matches the
    /// headline. TOP-N only, no "Others" row: the donut's uncovered arc reads
    /// as the long tail on its own (macOS v3). Each slice takes its provider's
    /// brand accent; the dollar figure still rides each row where the provider
    /// can be priced, and reads "—" where it cannot.
    public static IReadOnlyList<ModelShare> BuildTopModels(
        IEnumerable<(Model.DisplayProvider Provider, ModelSpend Spend)> spend, int top)
    {
        var all = spend.ToList();
        var tokenUniverse = Math.Max(1, all.Sum(m => m.Spend.Tokens));
        return all
            .Select(m => (m.Provider, m.Spend, Percent: m.Spend.Tokens / (double)tokenUniverse))
            .OrderByDescending(m => m.Percent)
            .Where(m => m.Percent >= 0.005)
            .Take(top)
            .Select(m => new ModelShare(
                m.Spend.Model, m.Spend.Tokens, m.Spend.Dollars, m.Percent,
                Model.ProviderIdentity.Accent(m.Provider), m.Provider))
            .ToList();
    }

    /// Whether a provider can state a dollar figure: Claude/Codex are
    /// table-priced and Grok self-reports; Cursor logs tokens with no model
    /// (no price) and Gemini ships no ledger. Mirrors CostPage.FaceOf so the
    /// overview shows "—" for Cursor, never a coined $0.
    public static bool ProvidesDollars(Model.DisplayProvider provider) =>
        provider is not (Model.DisplayProvider.Cursor or Model.DisplayProvider.Antigravity);

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

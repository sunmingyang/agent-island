namespace AgentIsland.Core;

public static class Formatting
{
    /// Single-unit compact duration: "45s", "12m", "3h", "4d" — matches the
    /// macOS Duration.compact used in reset countdowns and captions.
    public static string CompactDuration(TimeSpan span)
    {
        var seconds = Math.Max(0, span.TotalSeconds);
        if (seconds < 60) return $"{(int)seconds}s";
        if (seconds < 3600) return $"{(int)(seconds / 60)}m";
        if (seconds < 86400) return $"{(int)(seconds / 3600)}h";
        return $"{(int)(seconds / 86400)}d";
    }

    /// "$146.61" / "$1,510.80" — invariant thousands separators.
    public static string Money(double dollars) =>
        "$" + dollars.ToString("N2", System.Globalization.CultureInfo.InvariantCulture);

    /// "941", "12.4k", "211.2M", "2.17B".
    public static string CompactTokens(long tokens)
    {
        return tokens switch
        {
            < 1_000 => tokens.ToString(System.Globalization.CultureInfo.InvariantCulture),
            < 1_000_000 => Trim(tokens / 1_000.0) + "k",
            < 1_000_000_000 => Trim(tokens / 1_000_000.0) + "M",
            _ => Trim(tokens / 1_000_000_000.0) + "B",
        };

        static string Trim(double value) =>
            value.ToString(value >= 100 ? "0" : "0.#", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// Long-form "when it happens" countdown: "4小时后"/"3分钟后" in zh,
    /// "in 4h"/"in 3m" in en — the trigger-page reset caption format.
    public static string LongCountdown(TimeSpan until, bool chinese)
    {
        var seconds = Math.Max(0, until.TotalSeconds);
        if (chinese)
        {
            if (seconds < 60) return "1分钟内";
            if (seconds < 3600) return $"{(int)(seconds / 60)}分钟后";
            if (seconds < 86400) return $"{(int)(seconds / 3600)}小时后";
            return $"{(int)(seconds / 86400)}天后";
        }
        if (seconds < 60) return "under 1m";
        if (seconds < 3600) return $"in {(int)(seconds / 60)}m";
        if (seconds < 86400) return $"in {(int)(seconds / 3600)}h";
        return $"in {(int)(seconds / 86400)}d";
    }

    /// Relative "synced" label: "just now", "2m ago", "1h ago".
    public static string RelativeAgo(TimeSpan since, bool chinese)
    {
        var seconds = Math.Max(0, since.TotalSeconds);
        if (seconds < 60) return chinese ? "刚刚" : "just now";
        if (seconds < 3600)
        {
            var m = (int)(seconds / 60);
            return chinese ? $"{m} 分钟前" : $"{m}m ago";
        }
        var h = (int)(seconds / 3600);
        return chinese ? $"{h} 小时前" : $"{h}h ago";
    }
}

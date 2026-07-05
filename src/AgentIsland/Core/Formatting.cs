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

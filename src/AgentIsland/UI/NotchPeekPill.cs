using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using AgentIsland.Core;
using AgentIsland.UI.Theme;
using AgentIsland.Usage;

namespace AgentIsland.UI;

/// The peek-state glance readout: `32% · 2h` — 5h used percentage tinted in
/// the provider color, reset countdown in quiet white. Rendering rules match
/// the macOS NotchPeekPill: keep the last good value during refresh, `—%` on
/// error, dim window-length fallback when no live countdown exists.
public sealed class NotchPeekPill : TextBlock
{
    private TriggerTool _tool = TriggerTool.Claude;

    public NotchPeekPill()
    {
        FontFamily = new FontFamily("Cascadia Mono, Consolas");
        FontSize = 11;
        FontWeight = FontWeights.SemiBold;
        VerticalAlignment = VerticalAlignment.Center;
    }

    public TriggerTool Tool
    {
        get => _tool;
        set => _tool = value;
    }

    public void Update(WindowUsage usage, bool loading)
    {
        Inlines.Clear();
        var tint = IslandColors.For(_tool);

        if (usage.HasError && usage.UsedPercent == 0)
        {
            Inlines.Add(Dim("—%", 0.40));
            return;
        }
        if (loading && usage.UsedPercent == 0 && usage.ResetAt is null)
        {
            Inlines.Add(Dim("…", 0.55));
            return;
        }

        var percent = new Run($"{Math.Round(usage.UsedPercent * 100)}%")
        {
            Foreground = IslandColors.Brush(tint),
        };
        Inlines.Add(percent);

        var now = DateTimeOffset.Now;
        if (usage.ResetAt is { } resetAt && resetAt > now)
        {
            Inlines.Add(Dim(" · " + CompactCountdown(resetAt - now), 0.70));
        }
        else
        {
            // No live countdown: fall back to the window length at reduced
            // opacity so countdown vs. passive label stays visually distinct
            // without changing geometry.
            Inlines.Add(Dim(" · 5h", 0.40));
        }
    }

    public static string CompactCountdown(TimeSpan remaining)
    {
        // Nh when >= 1h remaining (floored — 107m reads "1h"), Nm under 1h.
        // Never mixed: "1h 47m" is too noisy for a glance pill.
        if (remaining.TotalHours >= 1) return $"{(int)remaining.TotalHours}h";
        return $"{Math.Max(1, (int)Math.Round(remaining.TotalMinutes))}m";
    }

    private static Run Dim(string text, double opacity) => new(text)
    {
        Foreground = IslandColors.Brush(IslandColors.White(opacity)),
    };
}

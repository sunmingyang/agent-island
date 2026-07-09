using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using AgentIsland.Core;

namespace AgentIsland.UI;

/// Draws the system-tray icon so it visualizes status at a glance — the
/// Windows-native home for the island's ambient signal. A dark disc holds a
/// usage ring (the busiest provider's 5-hour percentage), colored by urgency;
/// attention and your-turn states override the color and add a center dot so
/// the icon reads red/amber the moment something needs you.
internal static class TrayIconRenderer
{
    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    private static readonly Color AlarmRed = Color.FromArgb(0xF5, 0x57, 0x4A);
    private static readonly Color Amber = Color.FromArgb(0xF5, 0xA5, 0x24);
    private static readonly Color Teal = Color.FromArgb(0x3D, 0xD6, 0x8C);

    /// Ring color follows the usage urgency ladder unless a state overrides it.
    private static Color RingColor(ActivityState state, double usage5h)
    {
        if (state.IsAttentionState()) return AlarmRed;
        if (state == ActivityState.NeedsYou) return Amber;
        if (usage5h >= 0.90) return Color.FromArgb(0xE6, 0x5F, 0x5F);
        if (usage5h >= 0.70) return Color.FromArgb(0xE8, 0xA8, 0x5A);
        return Teal;
    }

    public static Icon Render(double usage5h, ActivityState state)
    {
        const int s = 32;
        var color = RingColor(state, usage5h);
        var showDot = state.IsAttentionState() || state == ActivityState.NeedsYou;

        using var bmp = new Bitmap(s, s);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            using (var disc = new SolidBrush(Color.FromArgb(235, 0x14, 0x14, 0x18)))
                g.FillEllipse(disc, 1, 1, s - 2, s - 2);

            const float pad = 5f;
            var rect = new RectangleF(pad, pad, s - 2 * pad, s - 2 * pad);
            using (var track = new Pen(Color.FromArgb(55, 255, 255, 255), 3.5f))
                g.DrawEllipse(track, rect);

            var sweep = (float)(Math.Clamp(usage5h, 0, 1) * 360);
            if (sweep >= 1f)
            {
                using var arc = new Pen(color, 3.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                g.DrawArc(arc, rect, -90, sweep);
            }

            if (showDot)
            {
                using var glow = new SolidBrush(Color.FromArgb(90, color.R, color.G, color.B));
                g.FillEllipse(glow, s / 2f - 6f, s / 2f - 6f, 12f, 12f);
                using var dot = new SolidBrush(color);
                g.FillEllipse(dot, s / 2f - 3.5f, s / 2f - 3.5f, 7f, 7f);
            }
        }

        // GetHicon leaks its HICON unless destroyed; clone into an owned Icon,
        // then free the temporary handle.
        var handle = bmp.GetHicon();
        try
        {
            using var shared = Icon.FromHandle(handle);
            return (Icon)shared.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }
}

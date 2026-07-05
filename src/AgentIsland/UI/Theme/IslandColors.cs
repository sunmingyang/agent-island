using System.Windows.Media;

namespace AgentIsland.UI.Theme;

/// Color tokens carried over from the macOS app (Sources/Theme/Colors.swift
/// and UrgencyColor.swift).
public static class IslandColors
{
    /// Claude terracotta — provider branding, logo tint, chart fills.
    public static readonly Color Claude = Color.FromRgb(0xCC, 0x78, 0x5C);

    /// Codex sky blue.
    public static readonly Color Codex = Color.FromRgb(0x5A, 0xA8, 0xF0);

    /// Loading sweep + idle/working glow.
    public static readonly Color Cobalt = Color.FromRgb(0x00, 0x47, 0xAB);

    /// Sync status dot.
    public static readonly Color LiveTeal = Color.FromRgb(0x3D, 0xD6, 0x8C);

    /// Warning threshold tint (70-89% usage, warning alerts).
    public static readonly Color AlertAmber = Color.FromRgb(0xF5, 0xA5, 0x24);

    /// Critical/attention tint (90%+ usage, stalled/auth/rate states).
    public static readonly Color AlertRed = Color.FromRgb(0xE5, 0x48, 0x4D);

    /// The island silhouette black.
    public static readonly Color SilhouetteBlack = Color.FromRgb(0x02, 0x02, 0x03);

    /// Turn alarm window background (0.020, 0.020, 0.027 in sRGB).
    public static readonly Color AlarmBackground = Color.FromRgb(0x05, 0x05, 0x07);

    public static Color White(double opacity) => Color.FromArgb((byte)(opacity * 255), 0xFF, 0xFF, 0xFF);

    /// Percent readout tint ladder (UrgencyColor.swift): white below 70%,
    /// amber 70-89%, red at 90%+.
    public static Color Urgency(double usedPercent) => usedPercent switch
    {
        >= 0.90 => Color.FromRgb(0xE6, 0x5F, 0x5F),
        >= 0.70 => Color.FromRgb(0xE8, 0xA8, 0x5A),
        _ => Colors.White,
    };

    public static SolidColorBrush Brush(Color color, double opacity = 1)
    {
        var brush = new SolidColorBrush(color) { Opacity = opacity };
        brush.Freeze();
        return brush;
    }

    public static Color For(Core.TriggerTool tool) => tool == Core.TriggerTool.Claude ? Claude : Codex;
}

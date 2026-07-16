using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AgentIsland.Core;
using AgentIsland.UI.Charts;
using AgentIsland.UI.Theme;

namespace AgentIsland.UI;

/// The freed half of a solo panel: the absent provider's bare mark and
/// name — a quiet nameplate, not a data widget (macOS SoloProviderBadge).
/// It replaced the per-model breakdown table, whose "5h" legend read as a
/// quota window Codex no longer has; per-model data returns with the
/// report-card redesign.
public sealed class SoloProviderBadge : Grid
{
    public SoloProviderBadge(TriggerTool provider)
    {
        var color = provider == TriggerTool.Claude ? IslandColors.Claude : IslandColors.Codex;
        var stack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            // The optical center sits a touch high of the geometric one
            // (macOS bottom padding 30).
            Margin = new Thickness(12, 0, 12, 30),
        };
        var mark = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("F1 " + (provider == TriggerTool.Claude
                ? BrandGeometry.ClaudePath
                : BrandGeometry.OpenAiPath)),
            Fill = IslandColors.Brush(IslandColors.Alpha(color, 0.95)),
            Width = 30,
            Height = 30,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                ShadowDepth = 0,
                BlurRadius = 30, // macOS shadow radius 10 is a gaussian sigma; ~3x here
                Color = color,
                Opacity = 0.30,
            },
        };
        stack.Children.Add(mark);
        stack.Children.Add(new TextBlock
        {
            Text = provider == TriggerTool.Claude ? "Claude Code" : "Codex",
            FontFamily = IslandFonts.Ui,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = IslandColors.Brush(IslandColors.White(0.88)),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 12, 0, 0),
        });
        Children.Add(stack);
    }
}

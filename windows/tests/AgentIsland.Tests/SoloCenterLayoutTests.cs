using AgentIsland.Model;
using AgentIsland.UI;

namespace AgentIsland.Tests;

/// Pins the solo-centering geometry: with exactly one provider visible (and
/// the setting on) the bar folds the hidden side away and shrinks to
/// SoloGap + one tab, in both placements; symmetric layout returns when both
/// providers are on, when both are off, or when the setting is disabled.
public static class SoloCenterLayoutTests
{
    public static void RunAll()
    {
        var visibility = ProviderVisibilityStore.Shared;
        var solo = SoloCenterStore.Shared;
        var position = IslandPositionStore.Shared;
        var alwaysShow = AlwaysShowUsageStore.Shared;
        var model = IslandModel.Shared;

        var savedClaude = visibility.ClaudeVisible;
        var savedCodex = visibility.CodexVisible;
        var savedSolo = solo.Enabled;
        var savedPlacement = position.Placement;
        var savedAlwaysShow = alwaysShow.Enabled;
        try
        {
            position.Placement = IslandPlacement.TopBar;
            alwaysShow.Enabled = false;
            solo.Enabled = true;
            visibility.ClaudeVisible = true;
            visibility.CodexVisible = true;

            Expect(!model.SoloCentering, "two visible providers are not solo");
            Expect(model.Size.Width == 276, $"symmetric top bar must be 200+38*2, got {model.Size.Width}");

            visibility.CodexVisible = false;
            Expect(model.SoloCentering, "exactly one visible provider is solo");
            Expect(model.Size.Width == 48, $"solo compact must be SoloGap+Tab (48), got {model.Size.Width}");
            Console.WriteLine("PASS solo collapse narrows the compact bar to 48");

            visibility.CodexVisible = true;
            visibility.ClaudeVisible = false;
            Expect(model.SoloCentering && model.Size.Width == 48,
                "solo works the same with Claude hidden");
            Console.WriteLine("PASS solo is side-agnostic");

            visibility.CodexVisible = false;
            Expect(!model.SoloCentering, "both hidden is not solo — keep the symmetric ghost layout");
            Expect(model.Size.Width == 276, $"both-hidden keeps symmetric width, got {model.Size.Width}");
            Console.WriteLine("PASS both-hidden stays symmetric");

            visibility.CodexVisible = true;
            visibility.ClaudeVisible = false;
            solo.Enabled = false;
            Expect(!model.SoloCentering, "the setting off must restore symmetric layout");
            Expect(model.Size.Width == 276, $"setting off keeps symmetric width, got {model.Size.Width}");
            Console.WriteLine("PASS disabling the setting restores symmetry");

            solo.Enabled = true;
            position.Placement = IslandPlacement.Floating;
            Expect(model.Size.Width == 48, $"solo floating also collapses to 48, got {model.Size.Width}");
            visibility.ClaudeVisible = true;
            Expect(model.Size.Width == 140, $"symmetric floating must be 64+38*2, got {model.Size.Width}");
            Console.WriteLine("PASS floating placement solo/symmetric widths hold");
        }
        finally
        {
            visibility.ClaudeVisible = savedClaude;
            visibility.CodexVisible = savedCodex;
            solo.Enabled = savedSolo;
            position.Placement = savedPlacement;
            alwaysShow.Enabled = savedAlwaysShow;
        }
        Console.WriteLine("SoloCenterLayoutTests GREEN");
    }

    private static void Expect(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}

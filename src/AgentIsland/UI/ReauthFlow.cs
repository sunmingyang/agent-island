using AgentIsland.Core;
using AgentIsland.Usage;

namespace AgentIsland.UI;

/// One-click re-authentication: spawn the provider's login command in a
/// visible terminal right away. Only when nothing can be launched (CLI truly
/// missing) does the branded dialog appear — with a Retry that runs the
/// whole flow again, so a mid-update CLI swap never dead-ends the user.
public static class ReauthFlow
{
    public static void Run(TriggerTool tool)
    {
        var spawned = tool == TriggerTool.Claude
            ? UsageStore.Shared.ReauthenticateClaude()
            : UsageStore.Shared.ReauthenticateCodex();
        if (spawned) return;

        var message = tool == TriggerTool.Claude
            ? Localization.L10n.Tr("Claude Code CLI not found. Log in from a terminal with: claude /login")
            : Localization.L10n.Tr("Codex CLI not found. Log in from a terminal with: codex login");
        IslandDialog.Show(
            tool,
            Localization.L10n.Tr("Re-authenticate"),
            message,
            primaryLabel: Localization.L10n.Tr("Retry"),
            primaryAction: () => Run(tool),
            secondaryLabel: Localization.L10n.Tr("I know"));
    }
}

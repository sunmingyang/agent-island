using AgentIsland.Core;
using AgentIsland.Usage;

namespace AgentIsland.UI;

/// One-click re-authentication. Claude goes browser-first: the PKCE loopback
/// flow needs no CLI, so the "CLI not found" dialog can only appear on the
/// final fallback (web login failed AND the terminal flow couldn't spawn).
/// Codex keeps the visible-terminal flow. The dialog's Retry runs the whole
/// flow again, so a mid-update CLI swap never dead-ends the user.
public static class ReauthFlow
{
    public static void Run(TriggerTool tool)
    {
        if (tool == TriggerTool.Claude)
        {
            UsageStore.Shared.ReauthenticateClaude(onCliMissing: () => ShowCliMissing(tool));
            return;
        }
        if (UsageStore.Shared.ReauthenticateCodex()) return;
        ShowCliMissing(tool);
    }

    private static void ShowCliMissing(TriggerTool tool)
    {
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

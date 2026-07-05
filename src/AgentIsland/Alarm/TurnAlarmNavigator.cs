using System.Diagnostics;
using System.Text.RegularExpressions;
using AgentIsland.Core;
using AgentIsland.Usage;

namespace AgentIsland.Alarm;

/// "Open session" routing: Claude Desktop threads go through the claude://
/// deep link the desktop app registers; CLI sessions reopen the interactive
/// resume command in a visible terminal.
public static class TurnAlarmNavigator
{
    public static void Open(TriggerTool provider, ActivityMonitor.ActiveThread thread)
    {
        var sessionId = Sanitize(thread.SessionId);
        if (sessionId.Length == 0) return;

        if (provider == TriggerTool.Claude)
        {
            if (thread.LaunchTarget == SessionLaunchTarget.ClaudeDesktop
                && TryOpenUri($"claude://resume?sessionId={sessionId}"))
            {
                return;
            }
            if (Trigger.CLILocator.Locate("claude") is { } claude)
            {
                RunResumeInTerminal(claude, $"--resume {sessionId}", thread.Cwd, "Claude resume");
            }
            return;
        }

        if (TryOpenUri($"codex://threads/{sessionId}"))
        {
            return;
        }
        if (Trigger.CLILocator.Locate("codex") is { } codex)
        {
            RunResumeInTerminal(codex, $"resume {sessionId}", thread.Cwd, "Codex resume");
        }
    }

    private static bool TryOpenUri(string uri)
    {
        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = uri,
                UseShellExecute = true,
            });
            return process is not null;
        }
        catch
        {
            // No handler registered for the scheme — fall through to the CLI.
            return false;
        }
    }

    private static void RunResumeInTerminal(string binary, string arguments, string cwd, string title)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c start \"{title}\" /D \"{ValidDirectory(cwd)}\" cmd /k \"\"{binary}\" {arguments}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            Process.Start(startInfo);
        }
        catch (Exception error)
        {
            Debug.WriteLine($"AgentIsland: resume launch failed: {error.Message}");
        }
    }

    private static string ValidDirectory(string cwd) =>
        !string.IsNullOrEmpty(cwd) && System.IO.Directory.Exists(cwd) ? cwd : IslandPaths.Home;

    /// Session ids feed deep links and shell commands; restrict to the safe
    /// alphabet before either.
    private static string Sanitize(string sessionId) =>
        Regex.Replace(sessionId, "[^A-Za-z0-9_-]", "");
}

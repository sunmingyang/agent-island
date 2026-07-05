using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using AgentIsland.Core;
using AgentIsland.Usage;

namespace AgentIsland.Alarm;

/// "Open thread" routing, mirroring the macOS navigator: fire the desktop
/// app's deep link best-effort AND force its window to the foreground —
/// on Windows, protocol activation alone never unburies an already-running
/// app. CLI sessions reopen the interactive resume command in a terminal.
public static class TurnAlarmNavigator
{
    public static void Open(TriggerTool provider, ActivityMonitor.ActiveThread thread)
    {
        var sessionId = Sanitize(thread.SessionId);
        if (sessionId.Length == 0) return;

        if (provider == TriggerTool.Claude)
        {
            if (thread.LaunchTarget == SessionLaunchTarget.ClaudeDesktop)
            {
                // The deep link lands on the exact thread when the app
                // handles it; the focus call guarantees the window at least
                // comes up. Either alone is not enough.
                var linked = TryOpenUri($"claude://resume?sessionId={sessionId}");
                var focused = FocusAppWindow("claude");
                if (linked || focused) return;
            }
            if (Trigger.CLILocator.Locate("claude") is { } claude)
            {
                RunResumeInTerminal(claude, $"--resume {sessionId}", thread.Cwd, "Claude resume");
            }
            return;
        }

        var codexLinked = TryOpenUri($"codex://threads/{sessionId}");
        var codexFocused = FocusAppWindow("Codex");
        if (codexLinked || codexFocused) return;
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

    /// Brings the named app's main window up: restores it when minimized and
    /// takes the foreground. Works because the click that got us here means
    /// our own window currently holds focus, so Windows permits the handoff.
    private static bool FocusAppWindow(string processName)
    {
        try
        {
            foreach (var process in Process.GetProcessesByName(processName))
            {
                var hwnd = process.MainWindowHandle;
                if (hwnd == IntPtr.Zero) continue;
                if (IsIconic(hwnd)) ShowWindow(hwnd, SW_RESTORE);
                SetForegroundWindow(hwnd);
                return true;
            }
        }
        catch
        {
        }
        return false;
    }

    private const int SW_RESTORE = 9;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

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

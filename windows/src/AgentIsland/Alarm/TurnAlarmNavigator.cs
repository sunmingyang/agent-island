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
    /// The foreground-sensitive work (deep link + SetForegroundWindow) runs
    /// SYNCHRONOUSLY on the caller's UI thread, which still holds the
    /// foreground at click time — so the OS permits the window handoff.
    /// Only the slow CLI fallback (CLILocator probes every PATH directory,
    /// seconds on a dead network share) is pushed off-thread; spawning a
    /// terminal doesn't need us to hold the foreground. Backgrounding the
    /// WHOLE thing raced the alarm's own Close and reintroduced the dead
    /// click P11 fixed.
    public static void Open(TriggerTool provider, ActivityMonitor.ActiveThread thread)
    {
        var sessionId = Sanitize(thread.SessionId);
        if (sessionId.Length == 0) return;
        var cwd = thread.Cwd;

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
            System.Threading.Tasks.Task.Run(() =>
            {
                if (Trigger.CLILocator.Locate("claude") is { } claude)
                {
                    RunResumeInTerminal(claude, $"--resume {sessionId}", cwd, "Claude resume");
                }
            });
            return;
        }

        // Codex sessions discovered on disk are CLI sessions — resume them
        // in a terminal. The desktop deep link is the no-CLI fallback, not
        // a hijack of a terminal workflow. Both the CLILocator probe and the
        // spawn are slow and foreground-independent, so run off-thread; the
        // rare no-CLI focus fallback tolerates a taskbar flash.
        System.Threading.Tasks.Task.Run(() =>
        {
            if (Trigger.CLILocator.Locate("codex") is { } codex)
            {
                RunResumeInTerminal(codex, $"resume {sessionId}", cwd, "Codex resume");
                return;
            }
            if (TryOpenUri($"codex://threads/{sessionId}"))
            {
                FocusAppWindow("Codex");
            }
        });
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
                // SetForegroundWindow is refused when we no longer hold the
                // foreground — report that honestly so the caller falls
                // through to the CLI resume instead of a dead click.
                if (SetForegroundWindow(hwnd)) return true;
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
        var directory = ValidDirectory(cwd);
        try
        {
            // Prefer Windows Terminal, like TerminalLauncher — a legacy
            // conhost window reads as broken to anyone who lives in wt.
            if (Trigger.CLILocator.Locate("wt") is { } wt)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = wt,
                    Arguments = $"new-tab --title \"{title}\" --startingDirectory \"{directory}\" cmd /k \"{binary}\" {arguments}",
                    UseShellExecute = false,
                });
                return;
            }
            var startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c start \"{title}\" /D \"{directory}\" cmd /k \"\"{binary}\" {arguments}\"",
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

    /// Session ids feed deep links and shell commands. Reject anything
    /// outside the safe alphabet outright — stripping characters would
    /// silently resume a DIFFERENT session id.
    private static string Sanitize(string sessionId) =>
        Regex.IsMatch(sessionId, "^[A-Za-z0-9_-]+$") ? sessionId : "";
}

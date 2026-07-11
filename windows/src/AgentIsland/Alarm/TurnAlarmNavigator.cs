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
    /// Resumes the session in a terminal via the provider's CLI — the same
    /// mechanism the auto-trigger engine uses and knows works. There is NO
    /// deep link that resumes a session by id (`claude://resume?sessionId=…`
    /// is fictional — the registered schemes only ever open NEW sessions), so
    /// the old .claudeDesktop branch merely surfaced Claude on its default
    /// view and never landed on the thread. All of it runs off the UI thread
    /// (CLILocator probes every PATH dir — seconds on a dead share); spawning
    /// a terminal doesn't need us to hold the foreground.
    /// Returns true only when a terminal (or, as a last resort, the desktop
    /// app) genuinely launched. Runs off the UI thread — CLILocator probes
    /// every PATH dir (seconds on a dead share) — so the caller awaits and
    /// keeps the alarm up until the result, surfacing an error on failure
    /// instead of vanishing with nothing opened.
    public static System.Threading.Tasks.Task<bool> Open(TriggerTool provider, ActivityMonitor.ActiveThread thread)
    {
        var sessionId = Sanitize(thread.SessionId);
        if (sessionId.Length == 0) return System.Threading.Tasks.Task.FromResult(false);
        var cwd = thread.Cwd;

        if (provider == TriggerTool.Claude)
        {
            // Desktop sessions: bring Claude Desktop forward, nothing more —
            // there is no deep link that lands on an existing conversation,
            // and a Desktop user clicking "Open thread" expects their Desktop
            // window, not a terminal popping up (mirrors the macOS split).
            // CLI sessions: resume for real via `claude --resume` from the
            // session's own cwd; that user lives in a terminal already.
            if (thread.LaunchTarget == SessionLaunchTarget.ClaudeDesktop)
            {
                // No deep link lands on an existing Claude conversation, so
                // compensate: put the session title on the clipboard (here,
                // on the caller's STA/UI thread - Clipboard requires it) so
                // finding the thread is one paste in Claude's search.
                if (!string.IsNullOrEmpty(thread.Label))
                {
                    try { System.Windows.Clipboard.SetText(thread.Label); } catch { }
                }
                return System.Threading.Tasks.Task.Run(() => FocusAppWindow("claude"));
            }
            return System.Threading.Tasks.Task.Run(() =>
            {
                if (Trigger.CLILocator.Locate("claude") is { } claude)
                {
                    return RunResumeInTerminal(claude, $"--resume {sessionId}", cwd, "Claude resume");
                }
                // No CLI on PATH — best effort: bring Claude Desktop forward
                // (it can't resume by id, but it's better than nothing).
                return FocusAppWindow("claude");
            });
        }

        // Codex sessions: the desktop deep link lands on the exact thread in
        // the app the user is looking at, so it goes FIRST (mirrors macOS).
        // A user chatting in the Codex desktop app must not get a terminal
        // popped at them just because the CLI happens to be installed. The
        // CLI resume is the fallback when no codex:// handler exists.
        return System.Threading.Tasks.Task.Run(() =>
        {
            if (TryOpenUri($"codex://threads/{sessionId}"))
            {
                FocusAppWindow("Codex");   // best effort; the URI already landed
                return true;
            }
            if (Trigger.CLILocator.Locate("codex") is { } codex)
            {
                return RunResumeInTerminal(codex, $"resume {sessionId}", cwd, "Codex resume");
            }
            return false;
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

    /// Opens a visible terminal that resumes the session. Uses the cmd.exe
    /// `start` launcher (reliable, known quoting) rather than Windows Terminal
    /// — wt's own tokenizer mangles the quoted binary path for paths with
    /// spaces and, worse, "succeeds" while showing a broken tab, so it can't
    /// be trusted for the one thing that must work. Returns whether the
    /// terminal actually launched.
    private static bool RunResumeInTerminal(string binary, string arguments, string cwd, string title)
    {
        var directory = ValidDirectory(cwd);
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c start \"{title}\" /D \"{directory}\" cmd /k \"\"{binary}\" {arguments}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            return true;
        }
        catch (Exception error)
        {
            Debug.WriteLine($"AgentIsland: resume launch failed: {error.Message}");
            return false;
        }
    }

    private static string ValidDirectory(string cwd) =>
        !string.IsNullOrEmpty(cwd) && System.IO.Directory.Exists(cwd) ? cwd : IslandPaths.Home;

    /// Session ids feed deep links and shell commands. Reject anything
    /// outside the safe alphabet outright — stripping characters would
    /// silently resume a DIFFERENT session id.
    internal static string Sanitize(string sessionId) =>
        Regex.IsMatch(sessionId, "^[A-Za-z0-9_-]+$") ? sessionId : "";
}

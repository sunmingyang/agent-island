using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Threading;
using AgentIsland.Core;
using AgentIsland.Usage;

namespace AgentIsland.Trigger;

/// Fires auto-triggers. Treats a changed fiveHour.resetAt for a provider as
/// that provider's window having reset, then resumes the target session via
/// the provider's CLI. Also runs a fixed-interval timer for EveryHours
/// triggers. Direct port of the macOS engine, including the persisted reset
/// baselines that catch up rollovers missed while the app was closed.
public sealed class TriggerEngine
{
    public static TriggerEngine Shared { get; } = new();
    private TriggerEngine()
    {
        _lastReset = LoadBaselines();
    }

    private const string BaselineKey = "AgentIsland.triggerResetBaselines";
    private Dictionary<TriggerTool, DateTimeOffset> _lastReset;
    private DispatcherTimer? _intervalTimer;

    public void Start()
    {
        UsageStore.Shared.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(UsageStore.Claude) or nameof(UsageStore.Codex))
            {
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(CheckResets);
            }
        };
        _intervalTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(60),
        };
        _intervalTimer.Tick += (_, _) => CheckIntervals();
        _intervalTimer.Start();
        CheckResets();
    }

    private static DateTimeOffset? ResetAt(TriggerTool tool) => tool == TriggerTool.Claude
        ? UsageStore.Shared.Claude.FiveHour.ResetAt
        : UsageStore.Shared.Codex.FiveHour.ResetAt;

    /// Fire AfterReset triggers exactly once per genuine 5-hour rollover.
    /// A genuine reset is the window's resetAt advancing to a later time
    /// AFTER the boundary we were tracking has actually elapsed:
    ///  - the first window we ever see only seeds the baseline (no fire);
    ///  - a resetAt merely sliding forward while still in the future (demo
    ///    recomputes it every refresh) is not a reset;
    ///  - because the baseline is persisted, a rollover missed while closed
    ///    or asleep is caught up once on the next launch.
    private void CheckResets()
    {
        foreach (var tool in new[] { TriggerTool.Claude, TriggerTool.Codex })
        {
            if (ResetAt(tool) is not { } current) continue;
            if (!_lastReset.TryGetValue(tool, out var previous))
            {
                SetBaseline(current, tool);   // first sighting — seed only
                continue;
            }
            if (current <= previous || previous > DateTimeOffset.Now) continue;
            SetBaseline(current, tool);
            foreach (var trigger in TriggerStore.Shared.Triggers
                .Where(t => t.Enabled && t.Tool == tool && t.Mode == TriggerMode.AfterReset))
            {
                Fire(trigger);
            }
        }
    }

    private void SetBaseline(DateTimeOffset date, TriggerTool tool)
    {
        _lastReset[tool] = date;
        SaveBaselines(_lastReset);
    }

    private static Dictionary<TriggerTool, DateTimeOffset> LoadBaselines()
    {
        var raw = Preferences.Get<Dictionary<string, double>?>(BaselineKey);
        var output = new Dictionary<TriggerTool, DateTimeOffset>();
        if (raw is null) return output;
        foreach (var (key, value) in raw)
        {
            if (Enum.TryParse<TriggerTool>(key, ignoreCase: true, out var tool))
            {
                output[tool] = DateTimeOffset.FromUnixTimeMilliseconds((long)value);
            }
        }
        return output;
    }

    private static void SaveBaselines(Dictionary<TriggerTool, DateTimeOffset> map)
    {
        var raw = map.ToDictionary(
            kv => kv.Key.RawValue(),
            kv => (double)kv.Value.ToUnixTimeMilliseconds());
        Preferences.Set(BaselineKey, raw);
    }

    private void CheckIntervals()
    {
        var now = DateTimeOffset.Now;
        foreach (var trigger in TriggerStore.Shared.Triggers
            .Where(t => t.Enabled && t.Mode == TriggerMode.EveryHours))
        {
            if (trigger.LastFired is not { } last)
            {
                // Seed so the first fire lands a full interval after the
                // trigger was created, not immediately.
                TriggerStore.Shared.MarkFired(trigger.Id, now);
                continue;
            }
            var interval = TimeSpan.FromHours(Math.Max(1, trigger.EveryHours));
            if (now >= last + interval)
            {
                // Advance the clock on the ATTEMPT, not only on a successful
                // spawn — otherwise a fire that Fire() blocks (kill switch,
                // untrusted project, invalid id/message, binary not found)
                // leaves LastFired stale and retries every 60s tick forever.
                TriggerStore.Shared.MarkFired(trigger.Id, now);
                Fire(trigger);
            }
        }
    }

    public sealed record ResumeCommand(string Binary, string Arguments, string Cwd)
    {
        public string Display => $"\"{Binary}\" {Arguments}";
    }

    /// Spawn the resume command detached and log to AppData. The safety
    /// ladder: never in demo/debug (synthetic usage moves reset timers every
    /// refresh — firing would burn real tokens on a loop), never with the
    /// kill switch off, never for untrusted projects.
    public void Fire(Trigger trigger)
    {
        if (AppEnvironment.Current != AppMode.Normal)
        {
            Debug.WriteLine($"AgentIsland trigger: suppressed fire in non-normal mode ({trigger.Label})");
            return;
        }
        var safety = TriggerSafetyStore.Shared;
        if (!safety.ExecutionEnabled)
        {
            LogStatus("blocked: trigger execution is off", trigger);
            return;
        }
        if (!safety.IsAllowed(trigger.Cwd))
        {
            LogStatus($"blocked: project is not trusted for auto-resume\n{Preview(trigger)}", trigger);
            return;
        }
        // The session id is interpolated unquoted into the cmd.exe command
        // line below. It comes from on-disk session metadata (a Codex
        // session_meta id, a Claude transcript filename) that a local
        // attacker can plant, so a value like `x&calc&` would inject a
        // command. Real ids are UUID/alphanumeric; reject anything else
        // before it reaches the shell (mirrors TurnAlarmNavigator.Sanitize).
        if (!IsSafeSessionId(trigger.SessionId))
        {
            LogStatus("blocked: invalid session id", trigger);
            return;
        }
        // The message is spliced into the cmd.exe command line too, and its
        // only escaping (Replace("\"","\\\"")) is inert in cmd.exe — a quote
        // or an &/|/^/%/redirect would break out and run injected commands.
        // Resume nudges are short natural-language strings; reject any that
        // carry cmd metacharacters rather than execute them.
        if (!IsSafeMessage(trigger.Message))
        {
            LogStatus("blocked: message contains characters unsafe for the shell", trigger);
            return;
        }
        if (Command(trigger, requireResolvedBinary: true) is not { } command)
        {
            LogStatus($"blocked: {trigger.Tool.RawValue()} binary not found", trigger);
            return;
        }

        try
        {
            Directory.CreateDirectory(IslandPaths.TriggerRunsDir);
            var logPath = Path.Combine(
                IslandPaths.TriggerRunsDir,
                $"{trigger.Id}_{DateTimeOffset.Now.ToUnixTimeSeconds()}.log");
            var cwd = !string.IsNullOrEmpty(command.Cwd) && Directory.Exists(command.Cwd)
                ? command.Cwd
                : IslandPaths.Home;
            // cmd wrapper both launches .cmd shims and owns the log
            // redirection, so the child keeps writing after we return.
            var startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"\"{command.Binary}\" {command.Arguments} >> \"{logPath}\" 2>&1\"",
                WorkingDirectory = cwd,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            Process.Start(startInfo);
            TriggerStore.Shared.MarkFired(trigger.Id);
            Debug.WriteLine($"AgentIsland trigger: fired {trigger.Label} [{trigger.Tool.RawValue()}] {trigger.SessionId}");
        }
        catch (Exception error)
        {
            LogStatus($"spawn failed: {error.Message}", trigger);
        }
    }

    public string Preview(Trigger trigger) =>
        Command(trigger, requireResolvedBinary: false)?.Display
        ?? Localization.L10n.Tr("Command unavailable");

    public void OpenLogsDirectory()
    {
        try
        {
            Directory.CreateDirectory(IslandPaths.TriggerRunsDir);
            Process.Start(new ProcessStartInfo
            {
                FileName = IslandPaths.TriggerRunsDir,
                UseShellExecute = true,
            });
        }
        catch
        {
        }
    }

    private static readonly Regex SessionIdPattern = new("^[A-Za-z0-9_-]+$", RegexOptions.Compiled);
    // cmd.exe metacharacters + newlines. `!`/`(`/`)`/backtick are inert under a
    // plain `cmd /c` (no delayed expansion), so they stay allowed.
    private static readonly Regex MessageBlocklist = new("[\"&|<>^%\r\n]", RegexOptions.Compiled);

    /// A session id is only ever spliced unquoted into a shell line, so it
    /// must match the safe alphabet exactly.
    internal static bool IsSafeSessionId(string sessionId) => SessionIdPattern.IsMatch(sessionId);

    /// A resume message must carry no cmd metacharacters before it reaches
    /// the shell.
    internal static bool IsSafeMessage(string message) => !MessageBlocklist.IsMatch(message);

    private static ResumeCommand? Command(Trigger trigger, bool requireResolvedBinary)
    {
        var binary = CLILocator.PathFor(trigger.Tool);
        if (requireResolvedBinary && binary is null) return null;
        var displayBinary = binary ?? trigger.Tool.RawValue();
        // The session id and message are both validated before Fire() reaches
        // the shell (SessionIdPattern / MessageBlocklist), so neither can carry
        // a cmd metacharacter here.
        var message = trigger.Message;
        var arguments = trigger.Tool == TriggerTool.Claude
            ? $"--resume {trigger.SessionId} -p \"{message}\" --dangerously-skip-permissions"
            : $"exec resume {trigger.SessionId} \"{message}\" --dangerously-bypass-approvals-and-sandbox --skip-git-repo-check";
        return new ResumeCommand(displayBinary, arguments, trigger.Cwd);
    }

    private static void LogStatus(string message, Trigger trigger)
    {
        try
        {
            Directory.CreateDirectory(IslandPaths.TriggerRunsDir);
            var logPath = Path.Combine(
                IslandPaths.TriggerRunsDir,
                $"{trigger.Id}_{DateTimeOffset.Now.ToUnixTimeSeconds()}.log");
            File.WriteAllText(logPath, message + "\n");
        }
        catch
        {
        }
        Debug.WriteLine($"AgentIsland trigger: {message}");
    }
}

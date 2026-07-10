using System.ComponentModel;
using System.Windows.Threading;
using AgentIsland.Alarm;
using AgentIsland.Usage;

namespace AgentIsland.Core;

/// Aggregates scanned sessions into one visible state per provider, mixing in
/// usage-fetch attention states. Poll every 6 seconds plus file-event kicks
/// throttled to ~2/s with a guaranteed trailing scan: a streaming transcript
/// writes many times per second, but the LAST write of a turn (end_turn /
/// task_complete) must never wait for the fallback poll.
public sealed class ActivityMonitor : INotifyPropertyChanged
{
    public static ActivityMonitor Shared { get; } = new();
    private ActivityMonitor() { }

    public sealed record ActiveThread(
        string SessionId,
        string Label,
        string Cwd,
        DateTimeOffset Modified,
        string? TranscriptPath,
        string? TurnKey,
        SessionLaunchTarget LaunchTarget);

    public event PropertyChangedEventHandler? PropertyChanged;

    private ActivityState _claude = ActivityState.Idle;
    private ActivityState _codex = ActivityState.Idle;
    private ActiveThread? _claudeThread;
    private ActiveThread? _codexThread;
    private ActivityState? _demoClaude;
    private ActivityState? _demoCodex;
    private ActivityState _rawClaude = ActivityState.Idle;
    private ActivityState _rawCodex = ActivityState.Idle;
    private Dictionary<string, DateTimeOffset> _lastWorking = new();

    public ActivityState Claude
    {
        get => _demoClaude ?? _claude;
        private set { _claude = value; Raise(nameof(Claude)); }
    }

    public ActivityState Codex
    {
        get => _demoCodex ?? _codex;
        private set { _codex = value; Raise(nameof(Codex)); }
    }

    public ActiveThread? ClaudeThread
    {
        get => _claudeThread;
        private set { _claudeThread = value; Raise(nameof(ClaudeThread)); }
    }

    public ActiveThread? CodexThread
    {
        get => _codexThread;
        private set { _codexThread = value; Raise(nameof(CodexThread)); }
    }

    public ActivityState StateFor(TriggerTool provider) =>
        provider == TriggerTool.Claude ? Claude : Codex;

    /// Pre-overlay scan state. The turn-alarm confirm gate must read this:
    /// the usage overlay (rateLimited/authRequired outrank needsYou) would
    /// otherwise swallow alarms exactly when the quota is exhausted or the
    /// network is down — the moments a finished turn most needs surfacing.
    public ActivityState RawStateFor(TriggerTool provider) =>
        provider == TriggerTool.Claude ? _rawClaude : _rawCodex;

    public ActiveThread? ThreadFor(TriggerTool provider) =>
        provider == TriggerTool.Claude ? ClaudeThread : CodexThread;

    public void Demo(ActivityState? state)
    {
        _demoClaude = state;
        _demoCodex = state;
        Raise(nameof(Claude));
        Raise(nameof(Codex));
    }

    private DispatcherTimer? _timer;
    private TranscriptEventStream? _eventStream;
    private Dispatcher? _dispatcher;
    private DateTimeOffset _lastEventKick = DateTimeOffset.MinValue;
    private bool _kickPending;
    private bool _scanInFlight;
    private bool _rescanQueued;

    public void Start()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        Tick();
        _timer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
        {
            Interval = TimeSpan.FromSeconds(6),
        };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();

        var stream = new TranscriptEventStream(() =>
        {
            // File events arrive on watcher threads; hop to the UI thread.
            _dispatcher?.BeginInvoke(EventKick);
        });
        stream.Start();
        _eventStream = stream;
    }

    private void EventKick()
    {
        var now = DateTimeOffset.UtcNow;
        var elapsed = now - _lastEventKick;
        if (elapsed >= TimeSpan.FromSeconds(0.5))
        {
            _lastEventKick = now;
            Tick();
            return;
        }
        if (_kickPending) return;
        _kickPending = true;
        var delay = TimeSpan.FromSeconds(Math.Max(0.5 - elapsed.TotalSeconds, 0.05));
        var trailing = new DispatcherTimer(DispatcherPriority.Background, _dispatcher!)
        {
            Interval = delay,
        };
        trailing.Tick += (_, _) =>
        {
            trailing.Stop();
            _kickPending = false;
            _lastEventKick = DateTimeOffset.UtcNow;
            Tick();
        };
        trailing.Start();
    }

    private void Tick()
    {
        // One scan at a time; a kick that lands mid-scan queues exactly one
        // follow-up so the trailing write of a turn is never dropped.
        if (_scanInFlight)
        {
            _rescanQueued = true;
            return;
        }
        _scanInFlight = true;
        var now = DateTimeOffset.UtcNow;
        var lastWorkingSnapshot = new Dictionary<string, DateTimeOffset>(_lastWorking);
        Task.Run(() => SessionScanner.MonitoringScan(now, lastWorkingSnapshot))
            .ContinueWith(task =>
            {
                var sessions = task.IsCompletedSuccessfully ? task.Result : new List<ScannedSession>();
                Apply(sessions, now);
                _scanInFlight = false;
                if (_rescanQueued)
                {
                    _rescanQueued = false;
                    Tick();
                }
            }, _dispatcher is { } d
                ? TaskScheduler.FromCurrentSynchronizationContext()
                : TaskScheduler.Default);
    }

    private void Apply(List<ScannedSession> sessions, DateTimeOffset now)
    {
        var claudeResult = BestSession(sessions, TriggerTool.Claude,
            thread => AgentReminderCenter.Shared.HasAcknowledged(TriggerTool.Claude, thread));
        var codexResult = BestSession(sessions, TriggerTool.Codex,
            thread => AgentReminderCenter.Shared.HasAcknowledged(TriggerTool.Codex, thread));
        // Usage-level attention (rate-limited / auth-required red) only
        // applies to providers switched ON in Settings. Someone who only
        // runs Claude keeps Codex hidden - its missing login must not
        // pulse the island red forever.
        var visibility = Model.ProviderVisibilityStore.Shared;
        var claude = visibility.ClaudeVisible
            ? OverlayUsageAttention(claudeResult.State, UsageStore.Shared.Claude)
            : claudeResult.State;
        var codex = visibility.CodexVisible
            ? OverlayUsageAttention(codexResult.State, UsageStore.Shared.Codex)
            : codexResult.State;
        UpdateLastWorking(sessions, now);
        _rawClaude = claudeResult.State;
        _rawCodex = codexResult.State;
        ClaudeThread = claudeResult.Thread;
        Claude = claude;
        AgentReminderCenter.Shared.Handle(TriggerTool.Claude, NeedsYouThreads(sessions, TriggerTool.Claude));
        CodexThread = codexResult.Thread;
        Codex = codex;
        AgentReminderCenter.Shared.Handle(TriggerTool.Codex, NeedsYouThreads(sessions, TriggerTool.Codex));
    }

    private void UpdateLastWorking(List<ScannedSession> sessions, DateTimeOffset now)
    {
        foreach (var session in sessions)
        {
            if (session.Status == ActivityState.Working && session.TranscriptPath is { } path)
                _lastWorking[path] = now;
        }
        var livePaths = sessions
            .Where(s => s.TranscriptPath is not null)
            .Select(s => s.TranscriptPath!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        _lastWorking = _lastWorking
            .Where(kv => livePaths.Contains(kv.Key))
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
    }

    private static ActivityState OverlayUsageAttention(ActivityState state, AppUsage usage)
    {
        if (UsageAttentionState(usage) is not { } attention) return state;
        return (int)attention > (int)state ? attention : state;
    }

    private static ActivityState? UsageAttentionState(AppUsage usage)
    {
        if (usage.FiveHour.UsedPercent >= 1 || usage.Weekly.UsedPercent >= 1)
            return ActivityState.RateLimited;
        var messages = new[] { usage.FiveHour.Error, usage.Weekly.Error }
            .Where(m => !string.IsNullOrEmpty(m))
            .Select(m => m!.ToLowerInvariant())
            .ToList();
        if (messages.Any(m => m.Contains("rate limited") || m.Contains("rate_limit")))
            return ActivityState.RateLimited;
        if (messages.Any(m =>
                ClaudeCredentials.IsAuthRecoverableError(m)
                || m.Contains("auth")
                || m.Contains("login")
                || m.Contains("no codex")))
        {
            return ActivityState.AuthRequired;
        }
        if (messages.Any(IsProviderOrNetworkError))
            return ActivityState.RateLimited;
        return null;
    }

    private static bool IsProviderOrNetworkError(string message) =>
        message.StartsWith("http ", StringComparison.Ordinal)
        || message.Contains("bad response")
        || message.Contains("parse error")
        || message.Contains("timed out")
        || message.Contains("timeout")
        || message.Contains("offline")
        || message.Contains("network")
        || message.Contains("internet")
        || message.Contains("connection")
        || message.Contains("cannot connect")
        || message.Contains("could not connect")
        || message.Contains("not connected")
        || message.Contains("dns")
        || message.Contains("ssl")
        || message.Contains("tls");

    /// A turn still waiting on the user outranks everything. But once the
    /// user acknowledged it, the turn is old news: it must not pin the logo
    /// in a static needsYou (masking a genuinely running sibling). Stalled
    /// stays above working so real anomalies surface; below unacked needsYou
    /// so it can't eat an actionable alarm.
    private static int SelectionPriority(ScannedSession session, Func<ActiveThread, bool> isAcknowledged) =>
        session.Status switch
        {
            ActivityState.NeedsYou => isAcknowledged(MakeThread(session)) ? 1 : 4,
            ActivityState.Stalled => 3,
            ActivityState.Working => 2,
            _ => 0,
        };

    private static (ActivityState State, ActiveThread? Thread) BestSession(
        List<ScannedSession> sessions,
        TriggerTool tool,
        Func<ActiveThread, bool> isAcknowledged)
    {
        var ranked = sessions
            .Where(s => s.Tool == tool)
            .Select(s => (Session: s, Priority: SelectionPriority(s, isAcknowledged)))
            .OrderByDescending(x => x.Priority)
            .ThenByDescending(x => x.Session.Modified)
            .ToList();
        if (ranked.Count == 0) return (ActivityState.Idle, null);
        var top = ranked[0].Session;
        var thread = top.Status == ActivityState.Idle ? null : MakeThread(top);
        return (top.Status, thread);
    }

    /// Every needsYou session, newest first. The reminder center tracks each
    /// finished turn separately, so one thread's alarm can never cancel or
    /// mask another's.
    private static List<ActiveThread> NeedsYouThreads(List<ScannedSession> sessions, TriggerTool tool) =>
        sessions
            .Where(s => s.Tool == tool && s.Status == ActivityState.NeedsYou)
            .OrderByDescending(s => s.Modified)
            .Select(MakeThread)
            .ToList();

    private static ActiveThread MakeThread(ScannedSession session) => new(
        session.SessionId,
        session.Label,
        session.Cwd,
        session.Modified,
        session.TranscriptPath,
        session.TurnKey,
        session.LaunchTarget);

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

using System.ComponentModel;
using System.Net.NetworkInformation;
using System.Windows.Threading;
using AgentIsland.Core;
using AgentIsland.Localization;

namespace AgentIsland.Usage;

/// Live provider usage published to the UI and the activity monitor. Direct
/// port of the macOS UsageStore: parallel fetch, error-merge that never
/// clobbers good values, 24h provider-stamped cache, re-auth polling, and a
/// refresh-on-reconnect network monitor.
public sealed class UsageStore : INotifyPropertyChanged
{
    public static UsageStore Shared { get; } = new();

    private const string CacheKey = "UsageStore.lastSuccessfulUsage.v1";
    private static readonly TimeSpan CacheMaxAge = TimeSpan.FromHours(24);

    private AppUsage _claude = AppUsage.Empty;
    private AppUsage _codex = AppUsage.Empty;
    private DateTimeOffset? _lastUpdated;
    private string? _refreshWarning;
    private bool _loading;
    private bool _claudeReauthInProgress;
    private bool _codexReauthInProgress;

    private DispatcherTimer? _pollTimer;
    private CancellationTokenSource? _refreshCts;
    private Task? _refreshTask;
    private CancellationTokenSource? _claudeReauthCts;
    private CancellationTokenSource? _codexReauthCts;
    private bool _networkMonitorArmed;
    private bool _lastNetworkAvailable = true;

    public event PropertyChangedEventHandler? PropertyChanged;

    private UsageStore()
    {
        if (AppEnvironment.IsDemo) return;
        if (LoadCachedSnapshot() is { } snapshot)
        {
            _claude = snapshot.Claude;
            _codex = snapshot.Codex;
            _lastUpdated = snapshot.UpdatedAt;
        }
    }

    public AppUsage Claude { get => _claude; private set { _claude = value; Raise(nameof(Claude)); } }
    public AppUsage Codex { get => _codex; private set { _codex = value; Raise(nameof(Codex)); } }
    public DateTimeOffset? LastUpdated { get => _lastUpdated; private set { _lastUpdated = value; Raise(nameof(LastUpdated)); } }
    public string? RefreshWarning { get => _refreshWarning; private set { _refreshWarning = value; Raise(nameof(RefreshWarning)); } }
    public bool Loading { get => _loading; private set { _loading = value; Raise(nameof(Loading)); } }

    /// Set while a `claude auth login` flow is in progress; the UI hides the
    /// re-auth button during this window so users don't double-tap.
    public bool ClaudeReauthInProgress { get => _claudeReauthInProgress; private set { _claudeReauthInProgress = value; Raise(nameof(ClaudeReauthInProgress)); } }
    public bool CodexReauthInProgress { get => _codexReauthInProgress; private set { _codexReauthInProgress = value; Raise(nameof(CodexReauthInProgress)); } }

    public void Refresh()
    {
        if (Loading) return;

        // Demo mode for screen recordings: skip the network entirely and
        // inject hand-tuned values. Reset times are recomputed each refresh
        // so the countdowns tick down naturally on camera.
        if (AppEnvironment.IsDemo)
        {
            var now = DateTimeOffset.Now;
            Claude = new AppUsage(
                new WindowUsage(
                    DemoDouble("AGENTISLAND_DEMO_CLAUDE_5H", 0.73),
                    now.AddMinutes(DemoMinutes("AGENTISLAND_DEMO_CLAUDE_RESET_MINUTES", 107)),
                    null),
                new WindowUsage(0.0 + DemoDouble("AGENTISLAND_DEMO_CLAUDE_WEEKLY", 0.81), now.AddSeconds(4 * 86400 + 11 * 3600), null),
                "max");
            Codex = new AppUsage(
                new WindowUsage(
                    DemoDouble("AGENTISLAND_DEMO_CODEX_5H", 0.67),
                    now.AddMinutes(DemoMinutes("AGENTISLAND_DEMO_CODEX_RESET_MINUTES", 143)),
                    null),
                new WindowUsage(DemoDouble("AGENTISLAND_DEMO_CODEX_WEEKLY", 0.76), now.AddSeconds(4 * 86400 + 18 * 3600), null),
                "pro");
            LastUpdated = now;
            RefreshWarning = null;
            return;
        }

        Loading = true;
        _refreshCts?.Cancel();
        var cts = new CancellationTokenSource();
        _refreshCts = cts;
        var dispatcher = Dispatcher.CurrentDispatcher;
        _refreshTask = Task.Run(async () =>
        {
            // Thread the token into the HTTP calls so a superseding refresh
            // (network-up mid-flight on a dead path) actually aborts the dead
            // request instead of letting it run to its own timeout.
            var codexTask = UsageFetcher.FetchCodex(cts.Token);
            var claudeTask = UsageFetcher.FetchClaude(cts.Token);
            var codexResult = await codexTask;
            var claudeResult = await claudeTask;

            await dispatcher.BeginInvoke(() =>
            {
                // Cancellation = the network monitor saw the path come up
                // while we were mid-flight on a dead one. Drop the dead-path
                // errors so the superseding refresh has nothing to overwrite.
                if (cts.IsCancellationRequested)
                {
                    Loading = false;
                    return;
                }

                var codexFailed = IsErrorOnly(codexResult);
                var claudeFailed = IsErrorOnly(claudeResult);
                var mergedCodex = MergedUsage(Codex, codexResult);
                var mergedClaude = MergedUsage(Claude, claudeResult);
                Codex = mergedCodex;
                Claude = mergedClaude;
                SaveCachedSnapshot(mergedClaude, mergedCodex);
                RefreshWarning = WarningFor(codexFailed, claudeFailed);
                LastUpdated = DateTimeOffset.Now;
                Loading = false;
            });
        }, CancellationToken.None);
    }

    private static double DemoDouble(string key, double fallback)
    {
        var raw = Environment.GetEnvironmentVariable(key);
        if (raw is null || !double.TryParse(raw, out var value)) return fallback;
        return Math.Min(1, Math.Max(0, value));
    }

    private static int DemoMinutes(string key, int fallback)
    {
        var raw = Environment.GetEnvironmentVariable(key);
        if (raw is null || !int.TryParse(raw, out var value)) return fallback;
        return Math.Max(1, value);
    }

    /// True when both windows have errors and zero values — nothing useful
    /// to show, so we keep whatever we had before.
    private static bool IsErrorOnly(AppUsage usage) =>
        usage.FiveHour.Error is not null && usage.Weekly.Error is not null
        && usage.FiveHour.UsedPercent == 0 && usage.Weekly.UsedPercent == 0;

    /// Don't clobber existing good values when a fetch returns an all-error
    /// result: preserve the last useful percentages but carry the new error
    /// forward so the UI admits the values are stale. If the existing value
    /// is itself error-only (cold start, series of failures), let the new
    /// error through.
    private static AppUsage MergedUsage(AppUsage existing, AppUsage fetched)
    {
        if (!IsErrorOnly(fetched) || IsErrorOnly(existing)) return fetched;
        var error = fetched.FiveHour.Error ?? fetched.Weekly.Error;
        return new AppUsage(
            new WindowUsage(existing.FiveHour.UsedPercent, existing.FiveHour.ResetAt, error),
            new WindowUsage(existing.Weekly.UsedPercent, existing.Weekly.ResetAt, error),
            existing.Plan);
    }

    private static string? WarningFor(bool codexFailed, bool claudeFailed) => (claudeFailed, codexFailed) switch
    {
        (true, true) => L10n.Tr("Usage refresh failed"),
        (true, false) => L10n.Tr("Claude stale"),
        (false, true) => L10n.Tr("Codex stale"),
        _ => null,
    };

    // MARK: - Cache

    private static UsageCacheSnapshot? LoadCachedSnapshot()
    {
        var snapshot = Preferences.Get<UsageCacheSnapshot?>(CacheKey);
        if (snapshot is null) return null;
        return UsageCachePolicy.RestoredSnapshot(snapshot, DateTimeOffset.Now, CacheMaxAge);
    }

    private static void SaveCachedSnapshot(
        AppUsage claude,
        AppUsage codex,
        bool fetchedClaude = true,
        bool fetchedCodex = true)
    {
        var existing = LoadCachedSnapshot();
        var snapshot = UsageCachePolicy.SnapshotForSave(
            claude, codex, existing, DateTimeOffset.Now, fetchedClaude, fetchedCodex);
        if (snapshot is null) return;
        Preferences.Set(CacheKey, snapshot);
    }

    // MARK: - Preview injection (status guide)

    /// Replace current values with hand-tuned percentages so the alert
    /// engine's pulse + tint behavior can be exercised without waiting for a
    /// real provider crossing. The next scheduled poll overwrites them.
    public void InjectPreviewUsage(double claudeFiveHour, double codexFiveHour)
    {
        var now = DateTimeOffset.Now;
        var fiveHourReset = now.AddSeconds(2 * 3600 + 14 * 60);
        var weeklyReset = now.AddSeconds(4 * 86400 + 6 * 3600);
        Claude = new AppUsage(
            new WindowUsage(claudeFiveHour, fiveHourReset, null),
            new WindowUsage(0.45, weeklyReset, null),
            Claude.Plan ?? "max");
        Codex = new AppUsage(
            new WindowUsage(codexFiveHour, fiveHourReset, null),
            new WindowUsage(0.30, weeklyReset, null),
            Codex.Plan ?? "pro");
        LastUpdated = now;
        RefreshWarning = null;
    }

    // MARK: - Re-auth

    /// Preferred path: the in-app browser login — PKCE + a loopback callback
    /// caught by our own listener, writing the fresh, fully-scoped token pair
    /// straight to the credentials file. No terminal, no manual code paste.
    /// On any web failure we fall back to the legacy `claude auth login`
    /// terminal flow (spawn + poll the file stamp), so a machine that can't
    /// run the loopback flow is no worse off than before. Only when even the
    /// terminal can't spawn (CLI truly missing) does `onCliMissing` fire —
    /// the web flow itself needs no CLI, so that's the one place the
    /// "CLI not found" dialog still makes sense.
    public void ReauthenticateClaude(Action? onCliMissing = null)
    {
        if (ClaudeReauthInProgress) return;
        ClaudeReauthInProgress = true;
        _claudeReauthCts?.Cancel();
        var cts = new CancellationTokenSource();
        _claudeReauthCts = cts;
        var dispatcher = Dispatcher.CurrentDispatcher;
        _ = Task.Run(async () =>
        {
            if (await ClaudeWebLogin.Shared.Start() is ClaudeWebLogin.Outcome.Success)
            {
                await FinishClaudeReauth(dispatcher);
                return;
            }

            // Legacy fallback: spawn `claude auth login` in a terminal and
            // wait for the credentials file to change. Poll the local file
            // stamp (cheap), then hit the usage API once when credentials
            // actually change; polling the endpoint itself can trip
            // Anthropic's rate limit and hide the real auth recovery behind
            // a fresh `rate limited` error.
            var initialStamp = ClaudeCredentials.CredentialsModificationStamp();
            if (!ClaudeCredentials.SpawnReauth())
            {
                await dispatcher.BeginInvoke(() =>
                {
                    ClaudeReauthInProgress = false;
                    onCliMissing?.Invoke();
                });
                return;
            }
            for (var i = 0; i < 40; i++)
            {
                try { await Task.Delay(TimeSpan.FromSeconds(3), cts.Token); }
                catch (TaskCanceledException) { return; }
                var currentStamp = ClaudeCredentials.CredentialsModificationStamp();
                if (currentStamp is null || currentStamp == initialStamp) continue;
                await FinishClaudeReauth(dispatcher);
                return;
            }
            await FinishClaudeReauth(dispatcher);
        });
    }

    public bool ReauthenticateCodex()
    {
        if (CodexReauthInProgress) return true;
        var initialStamp = CodexCredentials.AuthModificationStamp();
        if (!CodexCredentials.SpawnReauth()) return false;
        CodexReauthInProgress = true;
        _codexReauthCts?.Cancel();
        var cts = new CancellationTokenSource();
        _codexReauthCts = cts;
        var dispatcher = Dispatcher.CurrentDispatcher;
        _ = Task.Run(async () =>
        {
            for (var i = 0; i < 40; i++)
            {
                try { await Task.Delay(TimeSpan.FromSeconds(3), cts.Token); }
                catch (TaskCanceledException) { return; }
                var currentStamp = CodexCredentials.AuthModificationStamp();
                if (currentStamp is null || currentStamp == initialStamp) continue;
                await FinishCodexReauth(dispatcher);
                return;
            }
            await FinishCodexReauth(dispatcher);
        });
        return true;
    }

    private async Task FinishClaudeReauth(Dispatcher dispatcher)
    {
        var fetched = await UsageFetcher.FetchClaude();
        await dispatcher.BeginInvoke(() =>
        {
            var merged = MergedUsage(Claude, fetched);
            Claude = merged;
            SaveCachedSnapshot(merged, Codex, fetchedClaude: true, fetchedCodex: false);
            RefreshWarning = IsErrorOnly(fetched) ? L10n.Tr("Claude stale") : null;
            if (!IsErrorOnly(fetched)) LastUpdated = DateTimeOffset.Now;
            ClaudeReauthInProgress = false;
        });
    }

    private async Task FinishCodexReauth(Dispatcher dispatcher)
    {
        var fetched = await UsageFetcher.FetchCodex();
        await dispatcher.BeginInvoke(() =>
        {
            var merged = MergedUsage(Codex, fetched);
            Codex = merged;
            SaveCachedSnapshot(Claude, merged, fetchedClaude: false, fetchedCodex: true);
            RefreshWarning = IsErrorOnly(fetched) ? L10n.Tr("Codex stale") : null;
            if (!IsErrorOnly(fetched)) LastUpdated = DateTimeOffset.Now;
            CodexReauthInProgress = false;
        });
    }

    // MARK: - Auto refresh

    public void StartAutoRefresh()
    {
        StopAutoRefresh();
        Refresh();
        ArmTimer();
        RefreshIntervalStore.Shared.PropertyChanged += OnIntervalChanged;
        StartNetworkMonitor();
    }

    public void StopAutoRefresh()
    {
        _pollTimer?.Stop();
        _pollTimer = null;
        RefreshIntervalStore.Shared.PropertyChanged -= OnIntervalChanged;
        if (_networkMonitorArmed)
        {
            NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
            _networkMonitorArmed = false;
        }
    }

    private void OnIntervalChanged(object? sender, PropertyChangedEventArgs e) => ArmTimer();

    private void ArmTimer()
    {
        _pollTimer?.Stop();
        _pollTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(RefreshIntervalStore.Shared.Seconds),
        };
        _pollTimer.Tick += (_, _) => Refresh();
        _pollTimer.Start();
    }

    /// Trigger an immediate refresh when connectivity returns — closes the
    /// launch-at-login race where Wi-Fi is still associating when the first
    /// refresh fires. Without this, the panel sits at the cold-start state
    /// until the next scheduled poll (5–30 minutes away).
    private void StartNetworkMonitor()
    {
        _lastNetworkAvailable = NetworkInterface.GetIsNetworkAvailable();
        NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
        _networkMonitorArmed = true;
    }

    private void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
    {
        var was = _lastNetworkAvailable;
        _lastNetworkAvailable = e.IsAvailable;
        if (!e.IsAvailable || was) return;
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(async () =>
        {
            // Cancel any in-flight refresh — it was started on the dead path
            // and will return an error. Wait for it to finalize so its
            // loading=false lands before the replacement starts.
            _refreshCts?.Cancel();
            if (_refreshTask is { } task)
            {
                try { await task; } catch { }
            }
            Refresh();
        });
    }

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

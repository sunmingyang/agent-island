using System.ComponentModel;
using System.Windows.Threading;
using AgentIsland.Core;
using AgentIsland.Localization;
using AgentIsland.Model;

namespace AgentIsland.Usage;

/// Gemini's slice of the panel: the last fetched quota snapshot plus the
/// account identity from oauth_creds.json. Timer-free — it rides
/// UsageStore.Refresh()'s cadence via KickRefresh() behind a 120s attempt
/// floor, with a Preferences snapshot cache so relaunch doesn't blank the
/// strip.
public sealed class GeminiUsageStore : INotifyPropertyChanged
{
    public static GeminiUsageStore Shared { get; } = new();

    /// Clicking the strip goes to Google's own quota page — the only place
    /// that authoritatively explains what these buckets mean.
    public const string QuotaDocsUrl = "https://developers.google.com/gemini-code-assist/resources/quotas";

    private const string CacheKey = "GeminiUsageStore.lastSnapshot.v1";
    private static readonly TimeSpan CacheMaxAge = TimeSpan.FromHours(24);
    private static readonly TimeSpan MinAttemptGap = TimeSpan.FromSeconds(120);

    private GeminiQuotaSnapshot? _snapshot;
    private string? _statusCaption;
    private DateTimeOffset? _lastUpdated;
    private string? _accountEmail;
    private bool _loading;
    private DateTimeOffset? _lastAttempt;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// Detection is launch-static, same as the other providers.
    public GeminiAuthDetection Detection { get; }

    private GeminiUsageStore()
    {
        if (AppEnvironment.IsDemo)
        {
            if (!DemoGuestFixturesEnabled)
            {
                Detection = new GeminiAuthDetection.NotInstalled();
                return;
            }
            var now = DateTimeOffset.Now;
            Detection = new GeminiAuthDetection.OauthPersonal();
            _snapshot = new GeminiQuotaSnapshot(
                new[]
                {
                    new GeminiModelBucket("gemini-3-pro-preview", 0.43, now.AddSeconds(7 * 3600 + 24 * 60)),
                    new GeminiModelBucket("gemini-3-flash-preview", 0.18, now.AddSeconds(7 * 3600 + 24 * 60)),
                },
                "standard-tier",
                "Paid");
            _lastUpdated = now;
            return;
        }

        Detection = GeminiCredentials.Detect();
        if (Detection is not GeminiAuthDetection.OauthPersonal) return;
        LoadIdentity();
        if (Preferences.Get<CachedSnapshot?>(CacheKey) is not { } cached) return;
        if (cached.Snapshot is not { } stored) return;
        if (DateTimeOffset.Now - cached.UpdatedAt > CacheMaxAge) return;
        _snapshot = stored;
        _lastUpdated = cached.UpdatedAt;
    }

    public GeminiQuotaSnapshot? Snapshot
    {
        get => _snapshot;
        private set { _snapshot = value; Raise(nameof(Snapshot)); Raise(nameof(TierBadge)); }
    }

    /// Non-null while the latest fetch ended in anything but data. Values in
    /// Snapshot are the preserved last-good numbers in that case.
    public string? StatusCaption
    {
        get => _statusCaption;
        private set { _statusCaption = value; Raise(nameof(StatusCaption)); }
    }

    public DateTimeOffset? LastUpdated
    {
        get => _lastUpdated;
        private set { _lastUpdated = value; Raise(nameof(LastUpdated)); }
    }

    public string? AccountEmail
    {
        get => _accountEmail;
        private set { _accountEmail = value; Raise(nameof(AccountEmail)); }
    }

    public bool Loading
    {
        get => _loading;
        private set { _loading = value; Raise(nameof(Loading)); }
    }

    /// Tier chip for the Settings row / strip ("FREE", "PAID", or Google's
    /// own paid-tier name uppercased).
    public string? TierBadge => Snapshot?.TierLabel?.ToUpperInvariant();

    /// Non-null when settings.json declares api-key / vertex-ai. The Settings
    /// row says so outright — implying live data we cannot fetch would be the
    /// dishonest option.
    public string? UnsupportedAuthType =>
        Detection is GeminiAuthDetection.UnsupportedAuth unsupported ? unsupported.AuthType : null;

    public void KickRefresh()
    {
        if (AppEnvironment.IsDemo) return;
        if (Detection is not GeminiAuthDetection.OauthPersonal) return;
        if (!ProviderVisibilityStore.Shared.GeminiPanelShown) return;
        if (Loading) return;
        if (_lastAttempt is { } last && DateTimeOffset.Now - last < MinAttemptGap) return;

        _lastAttempt = DateTimeOffset.Now;
        Loading = true;
        var dispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        _ = Task.Run(async () =>
        {
            GeminiUsageFetcher.Outcome outcome;
            try
            {
                outcome = await GeminiUsageFetcher.Fetch();
            }
            catch (Exception error)
            {
                // A faulted fetch must never leave Loading latched — that
                // would freeze the strip until the app is relaunched.
                outcome = new GeminiUsageFetcher.Outcome.Failed(error.Message);
            }
            await dispatcher.BeginInvoke(() => Apply(outcome));
        });
    }

    private void Apply(GeminiUsageFetcher.Outcome outcome)
    {
        Loading = false;
        switch (outcome)
        {
            case GeminiUsageFetcher.Outcome.Success success:
                Snapshot = success.Snapshot;
                StatusCaption = null;
                LastUpdated = DateTimeOffset.Now;
                LoadIdentity();
                Persist(success.Snapshot);
                break;
            case GeminiUsageFetcher.Outcome.ReauthRequired:
                StatusCaption = L10n.Tr("sign in again — run gemini");
                break;
            case GeminiUsageFetcher.Outcome.NeedsCliInstall:
                StatusCaption = L10n.Tr("needs a local gemini-cli install");
                break;
            case GeminiUsageFetcher.Outcome.MigratedToAntigravity:
                // A verdict about the account, not a fetch error — drop any
                // stale numbers so the strip doesn't imply a live quota.
                Snapshot = null;
                StatusCaption = L10n.Tr("personal accounts moved to Antigravity — support coming in a later version");
                break;
            case GeminiUsageFetcher.Outcome.UnsupportedAuth:
                Snapshot = null;
                StatusCaption = L10n.Tr("this sign-in method isn't supported yet");
                break;
            case GeminiUsageFetcher.Outcome.Failed failed:
                // Keep the last good numbers; the caption admits staleness.
                StatusCaption = failed.Message;
                break;
            case GeminiUsageFetcher.Outcome.NotInstalled:
                Snapshot = null;
                StatusCaption = null;
                break;
        }
    }

    private void LoadIdentity() =>
        AccountEmail = GeminiCredentials.LoadCreds(GeminiCredentials.CredsFile)?.Email;

    private static void Persist(GeminiQuotaSnapshot fresh) =>
        Preferences.Set(CacheKey, new CachedSnapshot(fresh, DateTimeOffset.Now));

    /// The guest fixtures only exist for the recording rig; a plain demo run
    /// still shows the classic Claude/Codex island.
    private static bool DemoGuestFixturesEnabled =>
        AppEnvironment.IsDemo && Environment.GetEnvironmentVariable("AGENTISLAND_DEMO_GUESTS") == "1";

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// Cache envelope. Public so the JSON serializer binds it the same way it
    /// binds UsageCacheSnapshot.
    public sealed record CachedSnapshot(GeminiQuotaSnapshot? Snapshot, DateTimeOffset UpdatedAt);
}

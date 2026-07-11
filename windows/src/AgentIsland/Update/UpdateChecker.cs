using System.Net.Http;
using System.Text.Json;
using System.Windows.Threading;
using AgentIsland.Core;

namespace AgentIsland.Update;

/// Lightweight release-feed updater for the Windows port (no Sparkle here):
/// polls the GitHub latest-release endpoint, compares the tag against the
/// running assembly version, and surfaces a dialog with a Download button.
/// Notifies once per discovered version — dismissing it stays dismissed
/// until an even newer version appears. The macOS app gets the same job
/// done by Sparkle; this mirrors that contract with one HTTP call a day.
public sealed class UpdateChecker
{
    public static UpdateChecker Shared { get; } = new();
    private UpdateChecker() { }

    private const string LatestApi =
        "https://api.github.com/repos/tristan666666/agent-island/releases/latest";
    private const string ReleasesPage =
        "https://github.com/tristan666666/agent-island/releases/latest";
    private const string AutoCheckKey = "AgentIsland.autoCheckUpdates";
    private const string DismissedKey = "AgentIsland.dismissedUpdateVersion";

    private DispatcherTimer? _timer;
    private bool _checking;

    public static Version CurrentVersion =>
        typeof(UpdateChecker).Assembly.GetName().Version ?? new Version(0, 0, 0);

    public void Start()
    {
        if (AppEnvironment.Current != AppMode.Normal) return;
        // First check ~30s after launch (startup shouldn't race the network),
        // then daily. The toggle is honored at fire time, so flipping it off
        // takes effect without a restart.
        var initial = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        initial.Tick += (_, _) =>
        {
            initial.Stop();
            if (AutoCheckEnabled) _ = CheckAsync(userInitiated: false);
        };
        initial.Start();

        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromHours(24),
        };
        _timer.Tick += (_, _) =>
        {
            if (AutoCheckEnabled) _ = CheckAsync(userInitiated: false);
        };
        _timer.Start();
    }

    private static bool AutoCheckEnabled =>
        Preferences.Get<bool?>(AutoCheckKey) ?? true;

    /// "Check now" and the background timer share this. User-initiated checks
    /// always report an outcome (latest / newer / failed); background checks
    /// stay silent unless a new, not-yet-dismissed version exists.
    public async Task CheckAsync(bool userInitiated)
    {
        if (_checking) return;
        _checking = true;
        try
        {
            var latest = await FetchLatestVersion();
            if (latest is not { } found)
            {
                if (userInitiated)
                {
                    UI.IslandDialog.ShowApp(
                        "Agent Island",
                        Localization.L10n.Tr("Couldn't reach the release feed. Try again in a bit."));
                }
                return;
            }

            if (found.Version <= CurrentVersion)
            {
                if (userInitiated)
                {
                    UI.IslandDialog.ShowApp(
                        "Agent Island",
                        Localization.L10n.Tr("You're on the latest version."));
                }
                return;
            }

            // Background checks respect "already told you about this one".
            var dismissed = Preferences.Get<string?>(DismissedKey);
            if (!userInitiated && dismissed == found.Tag) return;

            UI.IslandDialog.ShowApp(
                Localization.L10n.TrFormat("Agent Island {0} is available", found.Tag),
                Localization.L10n.Tr("A new version is ready on GitHub Releases. The download is a zip — unpack and replace the app."),
                primaryLabel: Localization.L10n.Tr("Download"),
                primaryAction: () => OpenReleasesPage(),
                secondaryLabel: Localization.L10n.Tr("Later"));
            Preferences.Set(DismissedKey, found.Tag);
        }
        finally
        {
            _checking = false;
        }
    }

    private static async Task<(Version Version, string Tag)?> FetchLatestVersion()
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestApi);
            // GitHub's API rejects requests without a User-Agent.
            request.Headers.UserAgent.ParseAdd("AgentIsland-Windows-Updater");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await Usage.Http.Client.SendAsync(request);
            if (!response.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
            var tag = Jsonl.GetString(doc.RootElement, "tag_name");
            if (string.IsNullOrEmpty(tag)) return null;
            var parsed = ParseTag(tag!);
            if (parsed is not { } version) return null;
            return (version, tag!);
        }
        catch
        {
            return null;
        }
    }

    /// "v1.5.4" / "1.5.4" → Version(1,5,4). Anything unparsable is ignored
    /// rather than treated as an update.
    internal static Version? ParseTag(string tag)
    {
        var trimmed = tag.TrimStart('v', 'V');
        return Version.TryParse(trimmed, out var version) ? version : null;
    }

    private static void OpenReleasesPage()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = ReleasesPage,
                UseShellExecute = true,
            });
        }
        catch
        {
        }
    }
}

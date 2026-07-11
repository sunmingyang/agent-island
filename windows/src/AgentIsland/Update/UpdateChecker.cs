using System.Net.Http;
using System.Text.Json;
using System.Windows.Threading;
using AgentIsland.Core;

namespace AgentIsland.Update;

/// What one release-feed check learned: the version, its tag, and — when the
/// release carries a Windows zip for this machine's architecture — enough to
/// download it without a second API call.
public sealed record UpdateInfo(
    Version Version, string Tag, string? AssetName, string? AssetUrl, long AssetSize);

/// Release-feed updater for the Windows port (no Sparkle here): polls the
/// GitHub latest-release endpoint, compares the tag against the running
/// assembly version, and offers a one-click "Update &amp; Relaunch" that hands
/// off to UpdateInstaller. Releases without a matching zip asset fall back
/// to opening the releases page. Notifies once per discovered version —
/// dismissing it stays dismissed until an even newer version appears. The
/// macOS app gets the same job done by Sparkle; this mirrors that contract
/// with one HTTP call a day.
public sealed class UpdateChecker
{
    public static UpdateChecker Shared { get; } = new();
    private UpdateChecker() { }

    private const string LatestApi =
        "https://api.github.com/repos/tristan666666/agent-island/releases/latest";
    internal const string ReleasesPage =
        "https://github.com/tristan666666/agent-island/releases/latest";
    private const string AutoCheckKey = "AgentIsland.autoCheckUpdates";
    private const string DismissedKey = "AgentIsland.dismissedUpdateVersion";

    private DispatcherTimer? _timer;
    private bool _checking;

    /// Breadcrumb for scripted verification (Tests.exe update-check).
    public static string LastOutcome { get; private set; } = "";

    public static Version CurrentVersion =>
        typeof(UpdateChecker).Assembly.GetName().Version ?? new Version(0, 0, 0);

    /// "win-x64" / "win-arm64" — must match build.ps1's zip naming.
    internal static string RuntimeSuffix =>
        "win-" + System.Runtime.InteropServices.RuntimeInformation
            .ProcessArchitecture.ToString().ToLowerInvariant();

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
            var latest = await FetchLatestAsync();
            if (latest is not { } found)
            {
                LastOutcome = "feed-unreachable";
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
                LastOutcome = $"latest({found.Tag})";
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
            if (!userInitiated && dismissed == found.Tag)
            {
                LastOutcome = $"dismissed({found.Tag})";
                return;
            }

            LastOutcome = $"prompt({found.Tag}, asset={found.AssetName ?? "none"})";
            Preferences.Set(DismissedKey, found.Tag);

            // Scripted verification: skip the prompt and run the install.
            if (Environment.GetEnvironmentVariable("AGENTISLAND_DEBUG_UPDATE_AUTO") == "1")
            {
                _ = UpdateInstaller.RunAsync(found);
                return;
            }

            if (found.AssetUrl is not null)
            {
                UI.IslandDialog.ShowApp(
                    Localization.L10n.TrFormat("Agent Island {0} is available", found.Tag),
                    Localization.L10n.Tr("The update downloads in the background, then Agent Island relaunches on the new version."),
                    primaryLabel: Localization.L10n.Tr("Update & Relaunch"),
                    primaryAction: () => _ = UpdateInstaller.RunAsync(found),
                    secondaryLabel: Localization.L10n.Tr("Later"));
            }
            else
            {
                // Release exists but carries no zip for this architecture
                // (e.g. the Windows CI job hasn't attached it yet) — send
                // the user to the page rather than pretend nothing shipped.
                UI.IslandDialog.ShowApp(
                    Localization.L10n.TrFormat("Agent Island {0} is available", found.Tag),
                    Localization.L10n.Tr("A new version is ready on GitHub Releases. The download is a zip — unpack and replace the app."),
                    primaryLabel: Localization.L10n.Tr("Download"),
                    primaryAction: OpenReleasesPage,
                    secondaryLabel: Localization.L10n.Tr("Later"));
            }
        }
        finally
        {
            _checking = false;
        }
    }

    /// AGENTISLAND_UPDATE_FEED overrides the GitHub endpoint for scripted
    /// verification: an http(s) URL is fetched, anything else is read as a
    /// local file in the same JSON shape.
    internal static async Task<UpdateInfo?> FetchLatestAsync()
    {
        try
        {
            var overrideFeed = Environment.GetEnvironmentVariable("AGENTISLAND_UPDATE_FEED");
            if (!string.IsNullOrWhiteSpace(overrideFeed) &&
                !overrideFeed.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                return ParseRelease(await System.IO.File.ReadAllTextAsync(overrideFeed), RuntimeSuffix);
            }

            var api = string.IsNullOrWhiteSpace(overrideFeed) ? LatestApi : overrideFeed;
            using var request = new HttpRequestMessage(HttpMethod.Get, api);
            // GitHub's API rejects requests without a User-Agent.
            request.Headers.UserAgent.ParseAdd("AgentIsland-Windows-Updater");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await Usage.Http.Client.SendAsync(request);
            if (!response.IsSuccessStatusCode) return null;
            return ParseRelease(await response.Content.ReadAsStringAsync(), RuntimeSuffix);
        }
        catch
        {
            return null;
        }
    }

    /// One GitHub release JSON → UpdateInfo. The zip asset must match this
    /// machine's architecture (AgentIsland-{version}-{runtime}.zip); a
    /// release without one still reports its version so the fallback dialog
    /// can point at the page.
    internal static UpdateInfo? ParseRelease(string json, string runtime)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var tag = Jsonl.GetString(root, "tag_name");
            if (string.IsNullOrEmpty(tag)) return null;
            if (ParseTag(tag!) is not { } version) return null;

            string? assetName = null, assetUrl = null;
            long assetSize = 0;
            if (root.TryGetProperty("assets", out var assets) &&
                assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = Jsonl.GetString(asset, "name");
                    if (name is null) continue;
                    if (!name.StartsWith("AgentIsland-", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!name.EndsWith($"-{runtime}.zip", StringComparison.OrdinalIgnoreCase)) continue;
                    assetName = name;
                    assetUrl = Jsonl.GetString(asset, "browser_download_url");
                    if (asset.TryGetProperty("size", out var size) &&
                        size.ValueKind == JsonValueKind.Number)
                    {
                        assetSize = size.GetInt64();
                    }
                    break;
                }
            }
            return new UpdateInfo(version, tag!, assetName, assetUrl, assetSize);
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

    internal static void OpenReleasesPage()
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

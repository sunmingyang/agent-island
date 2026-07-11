using System.Windows;
using AgentIsland.Core;
using AgentIsland.UI;
using AgentIsland.Usage;

namespace AgentIsland;

public partial class App : System.Windows.Application
{
    public static App Instance => (App)Current;

    private IslandWindow? _island;
    private TrayIcon? _tray;
    private System.Threading.Mutex? _singleInstance;

    /// Two live copies sharing %APPDATA%\AgentIsland corrupt each other's
    /// preferences (each periodic save resurrects that instance's stale
    /// snapshot), so a second launch bows out. The wait is generous because
    /// the auto-updater's relaunch overlaps the old process by design —
    /// the new exe must outwait the old one's exit, not give up. Demo/debug
    /// copies skip the gate: running one beside the real app is a supported
    /// verification flow.
    private bool ClaimSingleInstance()
    {
        if (AppEnvironment.Current != AppMode.Normal) return true;
        _singleInstance = new System.Threading.Mutex(
            initiallyOwned: false, @"Local\AgentIsland.SingleInstance");
        try
        {
            return _singleInstance.WaitOne(TimeSpan.FromSeconds(10));
        }
        catch (System.Threading.AbandonedMutexException)
        {
            return true; // previous holder died without releasing — ours now
        }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (!ClaimSingleInstance())
        {
            Shutdown();
            return;
        }
        InstallCrashLogger();
        Model.AppLanguageStore.ApplyAtStartup();

        if (AppEnvironment.IsDemo)
        {
            ActivityMonitor.Shared.Demo(ActivityState.Working);
        }

        _island = new IslandWindow();
        _island.Show();

        _tray = new TrayIcon(
            showIsland: () => _island?.PopUp(),
            toggleIsland: () =>
            {
                if (_island is null) return;
                if (_island.IsVisible) _island.Hide(); else _island.Show();
            },
            openSettings: UI.SettingsWindow.Open,
            exit: () =>
            {
                _tray?.Dispose();
                Shutdown();
            });
        TrayIcon.Current = _tray;

        ActivityMonitor.Shared.Start();
        UsageStore.Shared.StartAutoRefresh();
        Alarm.UsageExhaustionAlarm.Shared.Start();
        Update.UpdateInstaller.CleanupAtStartup();
        Update.UpdateChecker.Shared.Start();
        Cost.CostStore.Shared.StartAutoRefresh();
        Trigger.TriggerEngine.Shared.Start();
        Model.AlertEngine.Shared.Start();

        // Scripted-verification hooks, mirroring the demo-only buttons on
        // macOS: never set in normal use.
        if (Environment.GetEnvironmentVariable("AGENTISLAND_DEBUG_OPEN_SETTINGS") == "1")
        {
            UI.SettingsWindow.Open();
        }
        // Render the island's live glow/sweep to a PNG for parity checks —
        // AGENTISLAND_DEBUG_ISLAND_STATE (working/stalled/needsYou/…) forces
        // the state first. Immune to the window occlusion a screen grab hits.
        var islandPng = Environment.GetEnvironmentVariable("AGENTISLAND_DEBUG_ISLAND_PNG");
        if (!string.IsNullOrEmpty(islandPng))
        {
            var stateName = Environment.GetEnvironmentVariable("AGENTISLAND_DEBUG_ISLAND_STATE");
            if (Enum.TryParse<ActivityState>(stateName, ignoreCase: true, out var forced))
            {
                ActivityMonitor.Shared.Demo(forced);
            }
            _island?.SaveVisualSnapshot(islandPng);
        }
        // "1" pops the Sparkle-style up-to-date card; any other value is a
        // PNG path the card renders itself into (works across virtual
        // desktops, where a screen grab can't see it).
        var updateDialogPreview = Environment.GetEnvironmentVariable("AGENTISLAND_DEBUG_UPDATE_DIALOG");
        if (!string.IsNullOrEmpty(updateDialogPreview))
        {
            var dialog = IslandDialog.ShowUpdate(
                Localization.L10n.Tr("You're up to date!"),
                Localization.L10n.TrFormat(
                    "AgentIsland {0} is currently the newest version available.",
                    Update.UpdateChecker.CurrentVersionDisplay),
                primaryLabel: Localization.L10n.Tr("OK"),
                secondaryLabel: Localization.L10n.Tr("Version History"));
            if (updateDialogPreview != "1") dialog.SaveSnapshot(updateDialogPreview);
        }
        if (Environment.GetEnvironmentVariable("AGENTISLAND_DEBUG_DIALOG") == "1")
        {
            IslandDialog.Show(
                TriggerTool.Claude,
                Localization.L10n.Tr("Re-authenticate"),
                Localization.L10n.Tr("Claude Code CLI not found. Log in from a terminal with: claude /login"),
                meta: new[]
                {
                    (Localization.L10n.Tr("Alarm provider"), "Claude"),
                    (Localization.L10n.Tr("Alarm thread"), "Agent Island Windows"),
                    (Localization.L10n.Tr("Alarm project"), "AgentIsland"),
                },
                primaryLabel: Localization.L10n.Tr("Retry"),
                secondaryLabel: Localization.L10n.Tr("I know"));
        }
    }

    /// Rebuild the island (and its expanded chrome / tray menu) so a language
    /// change lands everywhere immediately — the stores and monitors keep
    /// running untouched, only the labels are re-created in the new language.
    public void RebuildForLanguageChange()
    {
        var wasVisible = _island?.IsVisible ?? true;
        _island?.Close();
        _island = new IslandWindow();
        if (wasVisible) _island.Show();

        _tray?.Dispose();
        _tray = new TrayIcon(
            showIsland: () => _island?.PopUp(),
            toggleIsland: () =>
            {
                if (_island is null) return;
                if (_island.IsVisible) _island.Hide(); else _island.Show();
            },
            openSettings: UI.SettingsWindow.Open,
            exit: () =>
            {
                _tray?.Dispose();
                Shutdown();
            });
        TrayIcon.Current = _tray;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        base.OnExit(e);
    }

    private static void InstallCrashLogger()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            LogCrash(args.ExceptionObject as Exception, "AppDomain");
        Current.DispatcherUnhandledException += (_, args) =>
            LogCrash(args.Exception, "Dispatcher");
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LogCrash(args.Exception, "Task");
            args.SetObserved();
        };
    }

    private static void LogCrash(Exception? error, string source)
    {
        try
        {
            System.IO.Directory.CreateDirectory(Core.IslandPaths.AppSupportDir);
            var path = System.IO.Path.Combine(Core.IslandPaths.AppSupportDir, "crash.log");
            System.IO.File.AppendAllText(
                path,
                $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] {source}: {error}\n\n");
        }
        catch
        {
        }
    }
}

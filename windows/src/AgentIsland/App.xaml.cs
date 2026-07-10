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

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
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
        Cost.CostStore.Shared.StartAutoRefresh();
        Trigger.TriggerEngine.Shared.Start();
        Model.AlertEngine.Shared.Start();

        // Scripted-verification hooks, mirroring the demo-only buttons on
        // macOS: never set in normal use.
        if (Environment.GetEnvironmentVariable("AGENTISLAND_DEBUG_OPEN_SETTINGS") == "1")
        {
            UI.SettingsWindow.Open();
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

using System.Windows;
using AgentIsland.Core;
using AgentIsland.UI;
using AgentIsland.Usage;

namespace AgentIsland;

public partial class App : System.Windows.Application
{
    private IslandWindow? _island;
    private TrayIcon? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        InstallCrashLogger();

        if (AppEnvironment.IsDemo)
        {
            ActivityMonitor.Shared.Demo(ActivityState.Working);
        }

        _island = new IslandWindow();
        _island.Show();

        _tray = new TrayIcon(
            toggleIsland: () =>
            {
                if (_island is null) return;
                if (_island.IsVisible) _island.Hide(); else _island.Show();
            },
            openSettings: () => { /* settings window arrives with M8 */ },
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

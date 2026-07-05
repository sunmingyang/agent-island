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

        if (AppEnvironment.IsDemo)
        {
            SeedDemoData();
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

        ActivityMonitor.Shared.Start();
    }

    /// Screenshot-friendly synthetic data, mirroring the macOS demo mode:
    /// healthy-looking percentages, live countdowns, plan chips, and a
    /// spinning Claude logo.
    private static void SeedDemoData()
    {
        var now = DateTimeOffset.Now;
        UsageStore.Shared.Claude = new AppUsage(
            new WindowUsage(0.73, now.AddMinutes(107), null),
            new WindowUsage(0.81, now.AddDays(4), null),
            "max");
        UsageStore.Shared.Codex = new AppUsage(
            new WindowUsage(0.67, now.AddMinutes(143), null),
            new WindowUsage(0.76, now.AddDays(4), null),
            "pro");
        UsageStore.Shared.LastUpdated = now;
        ActivityMonitor.Shared.Demo(ActivityState.Working);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        base.OnExit(e);
    }
}

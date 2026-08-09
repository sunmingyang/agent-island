using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace AgentIsland.UI;

/// CI verification rig: AGENTISLAND_SNAPSHOT_DIR renders every major
/// surface — both report cards, the island compact and expanded, all seven
/// settings tabs — into the directory and exits. Run under
/// AGENTISLAND_DEMO=1 on a Windows runner, this is how the port gets
/// eyeballed frame by frame without a hand install.
public static class SnapshotSweep
{
    public static void Run(Application app, IslandWindow island, string dir)
    {
        Directory.CreateDirectory(dir);

        // A wedged sweep must never leave the runner hanging for the job
        // timeout.
        var kill = new DispatcherTimer { Interval = TimeSpan.FromSeconds(120) };
        kill.Tick += (_, _) => app.Shutdown();
        kill.Start();

        // Let demo data, layout, and the first paint settle.
        var start = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        start.Tick += (_, _) =>
        {
            start.Stop();
            try
            {
                Report.ReportWindow.WritePng(
                    Report.ReportWindow.Kind.Weekly, Path.Combine(dir, "report-weekly.png"));
                Report.ReportWindow.WritePng(
                    Report.ReportWindow.Kind.Monthly, Path.Combine(dir, "report-monthly.png"));
            }
            catch
            {
            }
            island.SaveVisualSnapshot(Path.Combine(dir, "island-compact.png"));
            island.PopUp();
            var expanded = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.6) };
            expanded.Tick += (_, _) =>
            {
                expanded.Stop();
                island.SaveVisualSnapshot(Path.Combine(dir, "island-expanded.png"));
                SettingsWindow.SnapshotAllTabs(dir, app.Shutdown);
            };
            expanded.Start();
        };
        start.Start();
    }
}

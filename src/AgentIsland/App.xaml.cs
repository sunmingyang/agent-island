using System.Windows;
using AgentIsland.Core;

namespace AgentIsland;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ActivityMonitor.Shared.Start();
        // Island window, tray icon, and stores arrive with their milestones.
    }
}

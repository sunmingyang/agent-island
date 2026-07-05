using System.IO;
using System.Windows;

namespace AgentIsland.UI;

/// System tray entry point — the Windows stand-in for the macOS menu-bar
/// presence. Hosts the quit action and a visibility toggle for the island.
public sealed class TrayIcon : IDisposable
{
    private readonly System.Windows.Forms.NotifyIcon _icon;

    public TrayIcon(Action toggleIsland, Action openSettings, Action exit)
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add(Localization.L10n.Tr("Show / Hide island"), null, (_, _) => toggleIsland());
        menu.Items.Add(Localization.L10n.Tr("Settings…"), null, (_, _) => openSettings());
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add(Localization.L10n.Tr("Quit Agent Island"), null, (_, _) => exit());

        _icon = new System.Windows.Forms.NotifyIcon
        {
            Text = "Agent Island",
            Visible = true,
            ContextMenuStrip = menu,
            Icon = LoadIcon(),
        };
        _icon.DoubleClick += (_, _) => toggleIsland();
    }

    private static System.Drawing.Icon LoadIcon()
    {
        try
        {
            var uri = new Uri("pack://application:,,,/Assets/agentisland_logo.png");
            using var stream = System.Windows.Application.GetResourceStream(uri)!.Stream;
            using var bitmap = new System.Drawing.Bitmap(stream);
            using var sized = new System.Drawing.Bitmap(bitmap, 32, 32);
            var handle = sized.GetHicon();
            return System.Drawing.Icon.FromHandle(handle);
        }
        catch
        {
            return System.Drawing.SystemIcons.Application;
        }
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}

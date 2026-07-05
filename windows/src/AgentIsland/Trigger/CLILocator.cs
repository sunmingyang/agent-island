using System.IO;
using AgentIsland.Core;

namespace AgentIsland.Trigger;

/// Resolves the claude/codex CLI by probing known Windows install locations
/// before falling back to a PATH scan — the counterpart of the macOS
/// locator's Homebrew/nvm probes. Claude Desktop's managed CLI lives under
/// %APPDATA%\Claude\claude-code\&lt;version&gt;\claude.exe and is never on PATH.
public static class CLILocator
{
    public static string? PathFor(TriggerTool tool) => Locate(tool.RawValue());

    public static string? Locate(string name)
    {
        var home = IslandPaths.Home;
        var roaming = IslandPaths.RoamingAppData;
        var candidates = new List<string>
        {
            Path.Combine(roaming, "npm", name + ".cmd"),
            Path.Combine(roaming, "npm", name + ".exe"),
            Path.Combine(home, ".local", "bin", name + ".exe"),
            Path.Combine(home, ".local", "bin", name + ".cmd"),
            Path.Combine(home, ".bun", "bin", name + ".exe"),
        };
        foreach (var candidate in candidates.Where(File.Exists))
        {
            return candidate;
        }

        if (name == "claude" && ManagedClaudeCli() is { } managed) return managed;

        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathVar.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var ext in new[] { ".cmd", ".exe", ".bat" })
            {
                try
                {
                    var candidate = Path.Combine(dir.Trim(), name + ext);
                    if (File.Exists(candidate)) return candidate;
                }
                catch
                {
                }
            }
        }
        return null;
    }

    /// Claude Desktop vendors the CLI per version; pick the newest.
    private static string? ManagedClaudeCli()
    {
        var root = Path.Combine(IslandPaths.RoamingAppData, "Claude", "claude-code");
        if (!Directory.Exists(root)) return null;
        try
        {
            return Directory.EnumerateDirectories(root)
                .OrderByDescending(dir => Path.GetFileName(dir), VersionAwareComparer.Instance)
                .Select(dir => Path.Combine(dir, "claude.exe"))
                .FirstOrDefault(File.Exists);
        }
        catch
        {
            return null;
        }
    }

    private sealed class VersionAwareComparer : IComparer<string?>
    {
        public static VersionAwareComparer Instance { get; } = new();

        public int Compare(string? x, string? y)
        {
            if (Version.TryParse(x, out var vx) && Version.TryParse(y, out var vy)) return vx.CompareTo(vy);
            return string.CompareOrdinal(x, y);
        }
    }
}

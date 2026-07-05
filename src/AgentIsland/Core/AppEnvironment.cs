namespace AgentIsland.Core;

public enum AppMode
{
    Normal,
    Demo,
    Debug,
}

/// Mode flags resolved once at launch. Demo injects synthetic data for
/// screenshots and must never fire real resume commands; the legacy
/// CODEXISLAND_* variables are still accepted as a fallback, matching macOS.
public static class AppEnvironment
{
    public static AppMode Current { get; } = Resolve();

    public static bool IsDemo => Current == AppMode.Demo;
    public static bool IsDebug => Current == AppMode.Debug;

    private static AppMode Resolve()
    {
        if (Flag("AGENTISLAND_DEMO") || Flag("CODEXISLAND_DEMO")) return AppMode.Demo;
        if (Flag("AGENTISLAND_DEBUG") || Flag("CODEXISLAND_DEBUG")) return AppMode.Debug;
        return AppMode.Normal;
    }

    private static bool Flag(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return value is "1" or "true" or "TRUE" or "yes";
    }
}

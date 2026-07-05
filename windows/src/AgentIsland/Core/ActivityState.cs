namespace AgentIsland.Core;

/// Per-provider visible state. Raw values are the urgency ranking used when
/// aggregating multiple sessions: idle &lt; working &lt; needsYou &lt; stalled &lt;
/// rateLimited &lt; authRequired.
public enum ActivityState
{
    Idle = 0,
    Working = 1,
    NeedsYou = 2,
    Stalled = 3,
    RateLimited = 4,
    AuthRequired = 5,
}

public static class ActivityStateExtensions
{
    public static bool IsAttentionState(this ActivityState state) => state is
        ActivityState.Stalled or ActivityState.RateLimited or ActivityState.AuthRequired;
}

public enum TriggerTool
{
    Claude,
    Codex,
}

public static class TriggerToolExtensions
{
    public static string Display(this TriggerTool tool) => tool == TriggerTool.Claude ? "Claude" : "Codex";
    public static string RawValue(this TriggerTool tool) => tool == TriggerTool.Claude ? "claude" : "codex";
}

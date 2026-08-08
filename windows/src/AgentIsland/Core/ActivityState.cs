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

    /// Attention states that pulse. AuthRequired is deliberately excluded:
    /// a login can stay pending for hours, and an endless red blink reads as
    /// a crash — it gets the static red treatment instead (macOS
    /// pulsesAttention semantics).
    public static bool PulsesAttention(this ActivityState state) => state is
        ActivityState.Stalled or ActivityState.RateLimited;

    /// Live activity — a session is running, or something needs you. The
    /// rotating comet sweep spins on these and rests otherwise: Idle and
    /// NeedsYou (finished, waiting for your reply) are steady states where a
    /// spinning comet reads as a stuck loader and needlessly drives the WPF
    /// render thread.
    public static bool IsActiveState(this ActivityState state) => state is
        ActivityState.Working
        or ActivityState.Stalled or ActivityState.RateLimited or ActivityState.AuthRequired;
}

/// The providers a session can belong to. Triggers are persisted as JSON
/// with the default numeric enum encoding, so members may only ever be
/// APPENDED — reordering rewrites every stored trigger's tool.
public enum TriggerTool
{
    Claude,
    Codex,
    Gemini,
    Grok,
    Cursor,
}

public static class TriggerToolExtensions
{
    public static readonly TriggerTool[] All =
    {
        TriggerTool.Claude,
        TriggerTool.Codex,
        TriggerTool.Gemini,
        TriggerTool.Grok,
        TriggerTool.Cursor,
    };

    public static string Display(this TriggerTool tool) => Model.ProviderIdentity.DisplayName(tool);

    public static string RawValue(this TriggerTool tool) => tool switch
    {
        TriggerTool.Claude => "claude",
        TriggerTool.Codex => "codex",
        TriggerTool.Gemini => "gemini",
        TriggerTool.Grok => "grok",
        TriggerTool.Cursor => "cursor",
        _ => tool.ToString().ToLowerInvariant(),
    };

    public static TriggerTool? FromRawValue(string? raw) => raw?.Trim().ToLowerInvariant() switch
    {
        "claude" => TriggerTool.Claude,
        "codex" => TriggerTool.Codex,
        "gemini" => TriggerTool.Gemini,
        "grok" => TriggerTool.Grok,
        "cursor" => TriggerTool.Cursor,
        _ => null,
    };

    /// Only Claude and Codex expose a `--resume &lt;id&gt;` CLI contract, so only
    /// they can carry auto-triggers. The other three still flow through the
    /// monitoring scan (island logo + turn alarms) — session STATUS is
    /// five-provider, session RESUME is two.
    public static bool SupportsAutoResume(this TriggerTool tool) =>
        tool is TriggerTool.Claude or TriggerTool.Codex;
}

using AgentIsland.Trigger;

namespace AgentIsland.Tests;

/// Pins the AfterReset rollover predicate — the heart of auto-resume. A
/// trigger must fire exactly when the tracked boundary has elapsed and the
/// provider reports a later one; never on demo-style forward slide of a
/// still-future boundary, never on an unchanged or regressed boundary.
public static class TriggerResetTests
{
    private static readonly DateTimeOffset Boundary = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);

    public static void RunAll()
    {
        Expect(
            TriggerEngine.IsGenuineReset(
                current: Boundary.AddHours(5), previous: Boundary, now: Boundary.AddMinutes(3)),
            "elapsed boundary + later current is a reset");
        Expect(
            !TriggerEngine.IsGenuineReset(
                current: Boundary.AddHours(5), previous: Boundary, now: Boundary.AddMinutes(-3)),
            "a still-future boundary sliding forward is not a reset");
        Expect(
            !TriggerEngine.IsGenuineReset(
                current: Boundary, previous: Boundary, now: Boundary.AddMinutes(3)),
            "an unchanged boundary is not a reset");
        Expect(
            !TriggerEngine.IsGenuineReset(
                current: Boundary.AddHours(-1), previous: Boundary, now: Boundary.AddMinutes(3)),
            "a regressed boundary is not a reset");
        Expect(
            TriggerEngine.IsGenuineReset(
                current: Boundary.AddDays(2), previous: Boundary, now: Boundary.AddDays(1)),
            "a rollover missed while the app was closed catches up");
        Console.WriteLine("TriggerResetTests GREEN");
    }

    private static void Expect(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}

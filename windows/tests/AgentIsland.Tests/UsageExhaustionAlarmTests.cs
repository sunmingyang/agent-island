using AgentIsland.Alarm;
using AgentIsland.Core;
using AgentIsland.Usage;

namespace AgentIsland.Tests;

/// Pins the quota-alarm contract (macOS UsageExhaustionAlarm semantics plus
/// the per-provider coalescing added on Windows): warmup swallows
/// pre-existing exhaustion, one alarm per reset cycle, jitter inside a cycle
/// can't re-fire, a new reset boundary re-arms — and windows of one provider
/// exhausting together (or in quick succession) pop a single alarm carrying
/// the latest reset, not one popup each.
public static class UsageExhaustionAlarmTests
{
    public static void RunAll()
    {
        var tests = new (string Name, Action Test)[]
        {
            ("exhausted window fires once per reset cycle", TestFiresOncePerCycle),
            ("warmup swallows pre-launch exhaustion", TestWarmupSwallowsExisting),
            ("advanced reset boundary re-arms the alarm", TestNewCycleRearms),
            ("errored window never fires", TestErroredWindowNeverFires),
            ("reminders off suppresses without consuming the key", TestDisabledSuppressesWithoutConsuming),
            ("alarm key matches the macOS shape", TestAlarmKeyShape),
            ("hidden provider never fires", TestHiddenProviderNeverFires),
            ("both windows exhausting together alarm once, on the later reset", TestBothWindowsCoalesce),
            ("window exhausting inside an alarmed stretch stays silent", TestBlockedStretchStaysSilent),
            ("a stretch ending re-arms the provider", TestStretchEndRearms),
        };

        foreach (var (name, test) in tests)
        {
            test();
            Console.WriteLine($"PASS {name}");
        }
        Console.WriteLine("UsageExhaustionAlarmTests GREEN");
    }

    private static void Expect(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static readonly DateTimeOffset ResetA = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
    private static readonly DateTimeOffset ResetB = DateTimeOffset.FromUnixTimeSeconds(1_800_018_000);
    private static readonly DateTimeOffset ResetWeekly = DateTimeOffset.FromUnixTimeSeconds(1_800_400_000);
    private static readonly DateTimeOffset T0 = ResetA.AddHours(-1);

    private static AppUsage Usage(double fiveHourPercent, DateTimeOffset? resetAt, string? error = null) =>
        new(new WindowUsage(fiveHourPercent, resetAt, error), new WindowUsage(0.1, null, null));

    private static AppUsage UsageBoth(
        double fiveHourPercent, DateTimeOffset? fiveReset,
        double weeklyPercent, DateTimeOffset? weeklyReset) =>
        new(new WindowUsage(fiveHourPercent, fiveReset, null),
            new WindowUsage(weeklyPercent, weeklyReset, null));

    private static (UsageExhaustionAlarm Alarm, List<string> Fired) Make()
    {
        var fired = new List<string>();
        var alarm = new UsageExhaustionAlarm(
            (provider, window, resetAt) =>
                fired.Add(UsageExhaustionAlarm.QuotaAlarmKey(provider, window, resetAt)));
        return (alarm, fired);
    }

    private static void TestHiddenProviderNeverFires()
    {
        var (alarm, fired) = Make();
        // Warmup with the provider still visible and healthy.
        alarm.Recompute(Usage(0.5, ResetA), AppUsage.Empty, remindersEnabled: true);
        // Claude exhausts while hidden in Settings: no alarm, and the key is
        // not consumed.
        alarm.Recompute(Usage(1.0, ResetA), AppUsage.Empty, remindersEnabled: true, claudeVisible: false);
        Expect(fired.Count == 0, "hidden provider must not alarm");
        // Switching the provider back on lets the same cycle fire normally.
        alarm.Recompute(Usage(1.0, ResetA), AppUsage.Empty, remindersEnabled: true);
        Expect(fired.Count == 1, "re-shown provider should fire for the still-exhausted cycle");
    }

    private static void TestFiresOncePerCycle()
    {
        var (alarm, fired) = Make();
        // Warmup sample: healthy.
        alarm.Recompute(Usage(0.5, ResetA), AppUsage.Empty, remindersEnabled: true);
        Expect(fired.Count == 0, "warmup must never fire");

        alarm.Recompute(Usage(1.0, ResetA), AppUsage.Empty, remindersEnabled: true);
        Expect(fired.Count == 1, "crossing to exhausted must fire exactly once");
        Expect(fired[0] == "exhausted-claude-fiveHour-1800000000", $"unexpected key {fired[0]}");

        // Same cycle: jitter under and back over 100% must not re-fire.
        alarm.Recompute(Usage(0.98, ResetA), AppUsage.Empty, remindersEnabled: true);
        alarm.Recompute(Usage(1.0, ResetA), AppUsage.Empty, remindersEnabled: true);
        Expect(fired.Count == 1, "jitter inside one reset cycle must not re-alarm");
    }

    private static void TestWarmupSwallowsExisting()
    {
        var (alarm, fired) = Make();
        // First real sample is ALREADY exhausted — that predates launch.
        alarm.Recompute(Usage(1.0, ResetA), AppUsage.Empty, remindersEnabled: true, now: T0);
        alarm.Recompute(Usage(1.0, ResetA), AppUsage.Empty, remindersEnabled: true, now: T0);
        Expect(fired.Count == 0, "exhaustion that predates launch must not alarm");

        // But the NEXT cycle — after the warmed-up stretch has ended — does.
        alarm.Recompute(Usage(1.0, ResetB), AppUsage.Empty, remindersEnabled: true,
            now: ResetA.AddMinutes(5));
        Expect(fired.Count == 1, "the next reset cycle must alarm normally");
    }

    private static void TestNewCycleRearms()
    {
        var (alarm, fired) = Make();
        alarm.Recompute(Usage(0.2, ResetA), AppUsage.Empty, remindersEnabled: true, now: T0);
        alarm.Recompute(Usage(1.0, ResetA), AppUsage.Empty, remindersEnabled: true, now: T0);
        alarm.Recompute(Usage(1.0, ResetB), AppUsage.Empty, remindersEnabled: true,
            now: ResetA.AddMinutes(5));
        Expect(fired.Count == 2, "an advanced reset boundary must re-arm the alarm");
        Expect(fired[1] == "exhausted-claude-fiveHour-1800018000", $"unexpected key {fired[1]}");
    }

    private static void TestBothWindowsCoalesce()
    {
        var (alarm, fired) = Make();
        alarm.Recompute(UsageBoth(0.5, ResetA, 0.9, ResetWeekly), AppUsage.Empty,
            remindersEnabled: true, now: T0);
        // Heavy use pushes BOTH windows over 100% in the same poll — the
        // user is blocked once, so exactly one popup, and it names the
        // weekly reset (the true unblock time).
        alarm.Recompute(UsageBoth(1.0, ResetA, 1.0, ResetWeekly), AppUsage.Empty,
            remindersEnabled: true, now: T0);
        Expect(fired.Count == 1, "both windows crossing together must pop exactly one alarm");
        Expect(fired[0] == "exhausted-claude-weekly-1800400000",
            $"the alarm must carry the later (weekly) reset, got {fired[0]}");
    }

    private static void TestBlockedStretchStaysSilent()
    {
        var (alarm, fired) = Make();
        alarm.Recompute(UsageBoth(0.5, ResetA, 0.9, ResetWeekly), AppUsage.Empty,
            remindersEnabled: true, now: T0);
        alarm.Recompute(UsageBoth(1.0, ResetA, 0.9, ResetWeekly), AppUsage.Empty,
            remindersEnabled: true, now: T0);
        Expect(fired.Count == 1, "five-hour crossing alarms");
        // The weekly window crosses 20 minutes later, while the user is
        // still blocked on the 5-hour reset: same event, no second popup.
        alarm.Recompute(UsageBoth(1.0, ResetA, 1.0, ResetWeekly), AppUsage.Empty,
            remindersEnabled: true, now: T0.AddMinutes(20));
        Expect(fired.Count == 1, "a window exhausting inside an alarmed stretch must stay silent");
    }

    private static void TestStretchEndRearms()
    {
        var (alarm, fired) = Make();
        alarm.Recompute(UsageBoth(0.5, ResetA, 0.9, ResetWeekly), AppUsage.Empty,
            remindersEnabled: true, now: T0);
        alarm.Recompute(UsageBoth(1.0, ResetA, 1.0, ResetWeekly), AppUsage.Empty,
            remindersEnabled: true, now: T0);
        Expect(fired.Count == 1, "coalesced stretch alarms once");
        // Past the weekly reset the stretch is over; a fresh 5-hour
        // exhaustion is a new event and must alarm again.
        alarm.Recompute(UsageBoth(1.0, ResetWeekly.AddHours(5), 0.2, ResetWeekly.AddDays(7)),
            AppUsage.Empty, remindersEnabled: true, now: ResetWeekly.AddMinutes(30));
        Expect(fired.Count == 2, "a new stretch after the fired reset passes must alarm");
    }

    private static void TestErroredWindowNeverFires()
    {
        var (alarm, fired) = Make();
        alarm.Recompute(Usage(0.2, ResetA), AppUsage.Empty, remindersEnabled: true);
        alarm.Recompute(Usage(1.0, ResetA, error: "HTTP 500"), AppUsage.Empty, remindersEnabled: true);
        Expect(fired.Count == 0, "a window with an error is not a trusted 100%");
    }

    private static void TestDisabledSuppressesWithoutConsuming()
    {
        var (alarm, fired) = Make();
        alarm.Recompute(Usage(0.2, ResetA), AppUsage.Empty, remindersEnabled: true);
        alarm.Recompute(Usage(1.0, ResetA), AppUsage.Empty, remindersEnabled: false);
        Expect(fired.Count == 0, "reminders off must suppress the quota alarm");
        // Re-enabling inside the same cycle still fires — the key was never
        // consumed while disabled (mirrors the macOS guard placement).
        alarm.Recompute(Usage(1.0, ResetA), AppUsage.Empty, remindersEnabled: true);
        Expect(fired.Count == 1, "re-enabling within the cycle must deliver the pending alarm");
    }

    private static void TestAlarmKeyShape()
    {
        Expect(
            UsageExhaustionAlarm.QuotaAlarmKey(TriggerTool.Codex, QuotaWindowKind.Weekly, ResetA)
                == "exhausted-codex-weekly-1800000000",
            "alarm key must match the macOS exhausted-<provider>-<window>-<unix> shape");
        Expect(
            UsageExhaustionAlarm.QuotaAlarmKey(TriggerTool.Claude, QuotaWindowKind.FiveHour, null)
                == "exhausted-claude-fiveHour-none",
            "a null reset stamps 'none', matching macOS");
    }
}

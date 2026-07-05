using AgentIsland.Usage;

namespace AgentIsland.Tests;

/// 1:1 port of the macOS UsageCachePolicyTests.
public static class UsageCachePolicyTests
{
    public static void RunAll()
    {
        var tests = new (string Name, Action Test)[]
        {
            ("error-bearing preserved usage is not cacheable", TestErrorBearingPreservedUsageIsNotCacheableAndDoesNotRenew),
            ("plan-only no-data is not cacheable", TestPlanOnlyNoDataIsNotCacheableOrRestorable),
            ("real usage caches only when fresh", TestRealUsageCachesAndStripsOnlyFreshNoErrorUsage),
            ("mixed provider preserve policy", TestMixedProviderPreservesExistingOnlyWhenPeerIsFresh),
            ("single-provider save does not renew peer", TestSingleProviderSaveDoesNotRenewUnfetchedPeer),
        };

        foreach (var (name, test) in tests)
        {
            test();
            Console.WriteLine($"PASS {name}");
        }
        Console.WriteLine("UsageCachePolicyTests GREEN");
    }

    private static void Expect(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static AppUsage Usage(
        double fiveHour = 0,
        double weekly = 0,
        DateTimeOffset? fiveHourReset = null,
        DateTimeOffset? weeklyReset = null,
        string? fiveHourError = null,
        string? weeklyError = null,
        string? plan = null) => new(
        new WindowUsage(fiveHour, fiveHourReset, fiveHourError),
        new WindowUsage(weekly, weeklyReset, weeklyError),
        plan);

    private static DateTimeOffset Epoch(long seconds) => DateTimeOffset.FromUnixTimeSeconds(seconds);

    private static void TestErrorBearingPreservedUsageIsNotCacheableAndDoesNotRenew()
    {
        var oldDate = Epoch(1_000);
        var now = Epoch(2_000);
        var existing = new UsageCacheSnapshot(
            Usage(fiveHour: 0.64, weekly: 0.27, plan: "max"),
            Usage(fiveHour: 0.41, weekly: 0.19, plan: "pro"),
            oldDate);
        var preservedAfterFailure = Usage(
            fiveHour: 0.64,
            weekly: 0.27,
            fiveHourError: "rate limited",
            weeklyError: "rate limited",
            plan: "max");

        Expect(
            UsageCachePolicy.CacheableCopy(preservedAfterFailure) is null,
            "error-bearing preserved usage must not produce a cache copy");
        Expect(
            UsageCachePolicy.SnapshotForSave(
                preservedAfterFailure,
                Usage(plan: "pro"),
                existing,
                now) is null,
            "failed refresh must not renew an existing snapshot timestamp");
    }

    private static void TestPlanOnlyNoDataIsNotCacheableOrRestorable()
    {
        var planOnly = Usage(plan: "pro");

        Expect(
            UsageCachePolicy.CacheableCopy(planOnly) is null,
            "plan-only 0% usage must not be cacheable");

        var restored = UsageCachePolicy.RestoredSnapshot(
            new UsageCacheSnapshot(planOnly, AppUsage.Empty, Epoch(1_000)),
            Epoch(1_100),
            TimeSpan.FromHours(24));
        Expect(restored is null, "plan-only/no-data snapshots must not restore as clean 0%");
    }

    private static void TestRealUsageCachesAndStripsOnlyFreshNoErrorUsage()
    {
        var reset = Epoch(3_000);
        var fresh = Usage(fiveHour: 0.12, weekly: 0, fiveHourReset: reset, plan: "pro");
        var copy = UsageCachePolicy.CacheableCopy(fresh);

        Expect(copy is not null, "fresh real usage must be cacheable");
        Expect(copy!.FiveHour.UsedPercent == 0.12, "cache copy should preserve percent");
        Expect(copy.FiveHour.ResetAt == reset, "cache copy should preserve reset");
        Expect(copy.FiveHour.Error is null && copy.Weekly.Error is null, "cache copy should strip errors");

        var withError = Usage(fiveHour: 0.12, fiveHourReset: reset, fiveHourError: "stale", plan: "pro");
        Expect(
            UsageCachePolicy.CacheableCopy(withError) is null,
            "error-bearing real usage must not cache as fresh");
    }

    private static void TestMixedProviderPreservesExistingOnlyWhenPeerIsFresh()
    {
        var oldDate = Epoch(1_000);
        var now = Epoch(2_000);
        var existing = new UsageCacheSnapshot(
            Usage(fiveHour: 0.74, weekly: 0.35, plan: "max"),
            Usage(fiveHour: 0.44, weekly: 0.22, plan: "pro"),
            oldDate);

        var freshClaude = Usage(fiveHour: 0.82, weekly: 0.38, plan: "max");
        var updated = UsageCachePolicy.SnapshotForSave(
            freshClaude,
            Usage(fiveHourError: "offline", weeklyError: "offline", plan: "pro"),
            existing,
            now);

        Expect(updated is not null, "fresh provider should allow snapshot save");
        Expect(updated!.ClaudeUpdatedAt == now, "fresh provider should renew only its own timestamp");
        Expect(updated.CodexUpdatedAt == oldDate, "failed provider should keep its original cache timestamp");
        Expect(updated.Claude.FiveHour.UsedPercent == 0.82, "fresh provider should be updated");
        Expect(updated.Codex.FiveHour.UsedPercent == 0.44, "other provider should be preserved from existing cache");

        var noFreshProvider = UsageCachePolicy.SnapshotForSave(
            Usage(fiveHourError: "offline", weeklyError: "offline", plan: "max"),
            Usage(plan: "pro"),
            existing,
            now);
        Expect(
            noFreshProvider is null,
            "existing provider values should not be preserved when neither current provider has fresh data");
    }

    private static void TestSingleProviderSaveDoesNotRenewUnfetchedPeer()
    {
        var oldDate = Epoch(1_000);
        var now = Epoch(2_000);
        var existing = new UsageCacheSnapshot(
            Usage(fiveHour: 0.74, weekly: 0.35, plan: "max"),
            Usage(fiveHour: 0.44, weekly: 0.22, plan: "pro"),
            oldDate);

        var currentClaudeThatWasNotFetched = Usage(fiveHour: 0.91, weekly: 0.64, plan: "max");
        var freshlyFetchedCodex = Usage(fiveHour: 0.18, weekly: 0.07, plan: "pro");
        var updated = UsageCachePolicy.SnapshotForSave(
            currentClaudeThatWasNotFetched,
            freshlyFetchedCodex,
            existing,
            now,
            fetchedClaude: false,
            fetchedCodex: true);

        Expect(updated is not null, "fresh single-provider fetch should save a snapshot");
        Expect(updated!.CodexUpdatedAt == now, "fetched provider should renew its own timestamp");
        Expect(updated.ClaudeUpdatedAt == oldDate, "unfetched peer must keep its original timestamp");
        Expect(updated.Claude.FiveHour.UsedPercent == 0.74, "unfetched peer must be preserved from cache");
        Expect(updated.Codex.FiveHour.UsedPercent == 0.18, "fetched provider should be updated");
    }
}

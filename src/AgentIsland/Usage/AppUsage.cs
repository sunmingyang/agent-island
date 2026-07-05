namespace AgentIsland.Usage;

/// One rate-limit window as rendered in the UI. UsedPercent is normalized to
/// [0, 1]; Error carries the caption shown in place of a value.
public sealed record WindowUsage(double UsedPercent, DateTimeOffset? ResetAt, string? Error)
{
    public static WindowUsage Unknown { get; } = new(0, null, "no data");

    public bool HasError => !string.IsNullOrEmpty(Error);
}

/// Usage for one provider: the 5-hour and weekly windows plus the plan tier
/// ("max", "pro", ...) surfaced as the chip badge next to the provider name.
public sealed record AppUsage(WindowUsage FiveHour, WindowUsage Weekly, string? Plan = null)
{
    public static AppUsage Empty { get; } = new(
        new WindowUsage(0, null, null),
        new WindowUsage(0, null, null));

    public static AppUsage ErrorPair(string message) => new(
        new WindowUsage(0, null, message),
        new WindowUsage(0, null, message));

    /// True when this fetch produced nothing usable — both windows errored
    /// and no percentage survived. Used to keep showing the last good value.
    public bool IsErrorOnly =>
        FiveHour.HasError && Weekly.HasError && FiveHour.UsedPercent == 0 && Weekly.UsedPercent == 0;
}

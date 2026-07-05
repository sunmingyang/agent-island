using AgentIsland.Core;

namespace AgentIsland.Cost;

/// One billable model call reconstructed from a local log line.
public sealed record TokenEvent(
    TriggerTool Provider,
    DateTimeOffset Timestamp,
    string Model,
    long InputTokens,
    long OutputTokens,
    long CacheCreationTokens,
    long CacheReadTokens)
{
    /// ccusage parity: everything that crossed the wire.
    public long WireTokens => InputTokens + OutputTokens + CacheCreationTokens + CacheReadTokens;

    /// Matches Anthropic's claude.ai stats panel, which excludes cache.
    public long BillableTokens => InputTokens + OutputTokens;

    public double Dollars => Pricing.Cost(this);
}

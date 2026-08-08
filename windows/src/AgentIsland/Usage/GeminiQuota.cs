using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AgentIsland.Core;

namespace AgentIsland.Usage;

/// One `retrieveUserQuota` bucket, normalized to the app's used-percent
/// vocabulary (the endpoint reports `remainingFraction`).
public sealed record GeminiModelBucket(string ModelId, double UsedPercent, DateTimeOffset? ResetAt = null)
{
    public bool IsFlash => ModelId.Contains("flash", StringComparison.OrdinalIgnoreCase);

    public bool IsPro =>
        ModelId.Contains("pro", StringComparison.OrdinalIgnoreCase)
        && !ModelId.Contains("flash", StringComparison.OrdinalIgnoreCase);
}

/// What the island renders for Gemini: the Pro-family bucket closest to its
/// limit as the main bar, Flash as the secondary, plus identity garnish.
/// Serializable so the last good values survive a relaunch via the cache —
/// shaped like UsageCacheSnapshot (parameterless ctor + init properties)
/// because that is what Preferences round-trips.
public sealed class GeminiQuotaSnapshot
{
    public IReadOnlyList<GeminiModelBucket> Buckets { get; init; } = Array.Empty<GeminiModelBucket>();

    /// Raw tier id from loadCodeAssist ("free-tier", "standard-tier"…).
    public string? TierId { get; init; }

    /// Display label — paidTier.name when Google provides one, else a
    /// mapping of the tier id.
    public string? TierLabel { get; init; }

    public GeminiQuotaSnapshot()
    {
    }

    public GeminiQuotaSnapshot(
        IReadOnlyList<GeminiModelBucket> buckets,
        string? tierId = null,
        string? tierLabel = null)
    {
        Buckets = buckets;
        TierId = tierId;
        TierLabel = tierLabel;
    }

    /// Pro family, lowest remaining (= highest used) wins the main bar.
    /// JsonIgnore keeps the cache from carrying a second copy of the bucket.
    [JsonIgnore]
    public GeminiModelBucket? PrimaryPro => Buckets.Where(bucket => bucket.IsPro).MaxBy(bucket => bucket.UsedPercent);

    /// Flash family, same lowest-remaining rule, for the secondary caption.
    [JsonIgnore]
    public GeminiModelBucket? SecondaryFlash => Buckets.Where(bucket => bucket.IsFlash).MaxBy(bucket => bucket.UsedPercent);
}

/// Decoders for the two `cloudcode-pa.googleapis.com/v1internal` payloads.
/// Absence is data here: an account can legitimately report zero buckets, and
/// that is not the same as a decode failure.
public static class GeminiQuotaParser
{
    public sealed record CodeAssistProfile(string? TierId, string? TierLabel, string? ProjectId);

    /// `POST v1internal:loadCodeAssist` — currentTier.id + the quota project.
    /// Google's own paid-tier name wins the label when present.
    public static CodeAssistProfile? ParseLoadCodeAssist(byte[] data)
    {
        try
        {
            using var doc = JsonDocument.Parse(data);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            var tierId = Jsonl.GetObject(root, "currentTier") is { } currentTier
                ? NonEmpty(Jsonl.GetString(currentTier, "id"))
                : null;
            var paidName = Jsonl.GetObject(root, "paidTier") is { } paidTier
                ? NonEmpty(Jsonl.GetString(paidTier, "name"))
                : null;
            return new CodeAssistProfile(
                tierId,
                paidName ?? TierLabel(tierId),
                NonEmpty(Jsonl.GetString(root, "cloudaicompanionProject")));
        }
        catch
        {
            return null;
        }
    }

    /// `POST v1internal:retrieveUserQuota` — buckets[{modelId,
    /// remainingFraction, resetTime}]. An empty/missing buckets array is a
    /// valid answer (fresh account); null means the payload didn't decode.
    public static IReadOnlyList<GeminiModelBucket>? ParseQuota(byte[] data)
    {
        try
        {
            using var doc = JsonDocument.Parse(data);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (!root.TryGetProperty("buckets", out var rows) || rows.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<GeminiModelBucket>();
            }
            var buckets = new List<GeminiModelBucket>();
            foreach (var row in rows.EnumerateArray())
            {
                if (NonEmpty(Jsonl.GetString(row, "modelId")) is not { } modelId) continue;
                var remaining = Number(row, "remainingFraction") ?? 1;
                buckets.Add(new GeminiModelBucket(
                    modelId,
                    Math.Min(1, Math.Max(0, 1 - remaining)),
                    // Same tolerant reader the Grok endpoints get: Google
                    // stamps resetTime with sub-millisecond fractions, and a
                    // dropped timestamp silently erases the reset countdown.
                    GrokTimestamp.Parse(Jsonl.GetString(row, "resetTime"))));
            }
            return buckets;
        }
        catch
        {
            return null;
        }
    }

    /// Google's June-2026 consumer shutdown answers with one of these instead
    /// of data. It's an account-state verdict, not an error.
    public static bool IsMigrationSignal(byte[] data)
    {
        if (data.Length == 0) return false;
        string text;
        try
        {
            text = Encoding.UTF8.GetString(data);
        }
        catch
        {
            return false;
        }
        return text.Contains("unsupported_client", StringComparison.OrdinalIgnoreCase)
            || text.Contains("ineligibletier", StringComparison.OrdinalIgnoreCase)
            || text.Contains("antigravity", StringComparison.OrdinalIgnoreCase);
    }

    public static string? TierLabel(string? tierId) => tierId switch
    {
        "standard-tier" or "g1-pro-tier" => "Paid",
        "enterprise-tier" => "Enterprise",
        "free-tier" => "Free",
        "legacy-tier" => "Legacy",
        // Unknown paid tiers (Google keeps minting names) still deserve a
        // badge: prettify the raw id instead of hiding a paid subscription.
        // "some-new-tier" -> "Some New".
        _ when tierId is not null && tierId.EndsWith("-tier", StringComparison.Ordinal) =>
            PrettyTier(tierId),
        _ => null,
    };

    private static string? PrettyTier(string tierId)
    {
        var words = tierId[..^5]
            .Split('-', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => char.ToUpperInvariant(w[0]) + w[1..])
            .ToArray();
        return words.Length == 0 ? null : string.Join(' ', words);
    }

    private static double? Number(JsonElement root, string property)
    {
        if (root.ValueKind != JsonValueKind.Object) return null;
        if (!root.TryGetProperty(property, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.Number => value.TryGetDouble(out var number) ? (double?)number : null,
            JsonValueKind.String => double.TryParse(
                value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                ? (double?)parsed
                : null,
            _ => null,
        };
    }

    private static string? NonEmpty(string? raw) => string.IsNullOrEmpty(raw) ? null : raw;
}

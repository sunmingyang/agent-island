using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using AgentIsland.Localization;

namespace AgentIsland.Usage;

/// Fetches the Gemini CLI's Code Assist quota buckets, authenticating with
/// %USERPROFILE%\.gemini\oauth_creds.json and refreshing through Google's
/// token endpoint when the access token is expired or rejected. Display-only
/// — no transcript monitoring, no alarms live here.
public static class GeminiUsageFetcher
{
    public abstract record Outcome
    {
        public sealed record Success(GeminiQuotaSnapshot Snapshot) : Outcome;

        /// Refresh path exhausted — the user has to run `gemini` and re-auth.
        public sealed record ReauthRequired : Outcome;

        /// A refresh was required but no client id/secret could be extracted
        /// from a local gemini-cli install (and no env override).
        public sealed record NeedsCliInstall : Outcome;

        /// Google's consumer-tier shutdown verdict — the account moved to
        /// Antigravity. A state, not an error.
        public sealed record MigratedToAntigravity : Outcome;

        public sealed record Failed(string Message) : Outcome;

        /// settings.json declares api-key / vertex-ai.
        public sealed record UnsupportedAuth(string AuthType) : Outcome;

        /// No oauth_creds.json — Gemini CLI never signed in on this machine.
        public sealed record NotInstalled : Outcome;
    }

    private const string LoadCodeAssistUrl = "https://cloudcode-pa.googleapis.com/v1internal:loadCodeAssist";
    private const string QuotaUrl = "https://cloudcode-pa.googleapis.com/v1internal:retrieveUserQuota";
    private const string TokenUrl = "https://oauth2.googleapis.com/token";

    /// Per-request ceilings below the shared HttpClient's own timeout: a
    /// wedged tunnel must never hang the poll loop.
    private static readonly TimeSpan QuotaTimeout = TimeSpan.FromSeconds(25);
    private static readonly TimeSpan RefreshTimeout = TimeSpan.FromSeconds(20);

    public static async Task<Outcome> Fetch(CancellationToken ct = default)
    {
        var detection = GeminiCredentials.Detect();
        if (detection is GeminiAuthDetection.UnsupportedAuth unsupported)
        {
            return new Outcome.UnsupportedAuth(unsupported.AuthType);
        }
        if (detection is not GeminiAuthDetection.OauthPersonal) return new Outcome.NotInstalled();

        var credsPath = GeminiCredentials.CredsFile;
        if (GeminiCredentials.LoadCreds(credsPath) is not { } creds) return new Outcome.NotInstalled();

        // Proactive refresh near expiry (Google access tokens live ~1h).
        if (GeminiCredentials.NeedsRefresh(creds) && creds.RefreshToken is not null)
        {
            switch (await Refresh(creds, credsPath, ct))
            {
                case RefreshResult.Refreshed refreshed:
                    creds = refreshed.Creds;
                    break;
                case RefreshResult.NoClientCredentials:
                    return new Outcome.NeedsCliInstall();
                default:
                    // Clock skew can make a working token look expired — still try.
                    break;
            }
        }

        var first = await FetchSnapshot(creds.AccessToken, ct);
        if (first is not SnapshotResult.Unauthorized) return ToOutcome(first);

        if (creds.RefreshToken is null) return new Outcome.ReauthRequired();
        switch (await Refresh(creds, credsPath, ct))
        {
            case RefreshResult.NoClientCredentials:
                return new Outcome.NeedsCliInstall();
            case RefreshResult.Refreshed refreshed:
                return ToOutcome(await FetchSnapshot(refreshed.Creds.AccessToken, ct));
            default:
                return new Outcome.ReauthRequired();
        }
    }

    // MARK: - Quota

    private abstract record SnapshotResult
    {
        public sealed record Success(GeminiQuotaSnapshot Snapshot) : SnapshotResult;
        public sealed record Migrated : SnapshotResult;
        public sealed record Unauthorized : SnapshotResult;
        public sealed record Failed(string Message) : SnapshotResult;
    }

    /// An Unauthorized that reaches here has already spent its retry, so it
    /// resolves to re-auth rather than another refresh round.
    private static Outcome ToOutcome(SnapshotResult result) => result switch
    {
        SnapshotResult.Success success => new Outcome.Success(success.Snapshot),
        SnapshotResult.Migrated => new Outcome.MigratedToAntigravity(),
        SnapshotResult.Failed failed => new Outcome.Failed(failed.Message),
        _ => new Outcome.ReauthRequired(),
    };

    private static async Task<SnapshotResult> FetchSnapshot(string token, CancellationToken ct)
    {
        var profileBody = new JsonObject
        {
            ["metadata"] = new JsonObject
            {
                ["ideType"] = "GEMINI_CLI",
                ["pluginType"] = "GEMINI",
            },
        };

        GeminiQuotaParser.CodeAssistProfile? profile = null;
        switch (await Post(LoadCodeAssistUrl, token, profileBody.ToJsonString(), ct))
        {
            case HttpOutcome.Transport transport:
                return new SnapshotResult.Failed(transport.Message);
            case HttpOutcome.Response { Status: 401 or 403 }:
                return new SnapshotResult.Unauthorized();
            case HttpOutcome.Response response:
                if (GeminiQuotaParser.IsMigrationSignal(response.Body)) return new SnapshotResult.Migrated();
                // The tier/project call is garnish for the quota call — a
                // non-200 here still lets the bucket fetch try with no project.
                if (response.Status == 200) profile = GeminiQuotaParser.ParseLoadCodeAssist(response.Body);
                break;
        }

        var quotaBody = new JsonObject();
        if (profile?.ProjectId is { } projectId) quotaBody["project"] = projectId;
        switch (await Post(QuotaUrl, token, quotaBody.ToJsonString(), ct))
        {
            case HttpOutcome.Transport transport:
                return new SnapshotResult.Failed(transport.Message);
            case HttpOutcome.Response { Status: 401 or 403 }:
                return new SnapshotResult.Unauthorized();
            case HttpOutcome.Response response:
                if (GeminiQuotaParser.IsMigrationSignal(response.Body)) return new SnapshotResult.Migrated();
                if (response.Status != 200) return new SnapshotResult.Failed($"http {response.Status}");
                if (GeminiQuotaParser.ParseQuota(response.Body) is not { } buckets)
                {
                    return new SnapshotResult.Failed("parse error");
                }
                return new SnapshotResult.Success(
                    new GeminiQuotaSnapshot(buckets, profile?.TierId, profile?.TierLabel));
        }

        return new SnapshotResult.Failed("bad response");
    }

    private abstract record HttpOutcome
    {
        public sealed record Response(int Status, byte[] Body) : HttpOutcome;
        public sealed record Transport(string Message) : HttpOutcome;
    }

    private static async Task<HttpOutcome> Post(string url, string token, string json, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(QuotaTimeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
            request.Headers.TryAddWithoutValidation("Accept", "application/json");
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await Http.Client.SendAsync(request, timeout.Token);
            var body = await response.Content.ReadAsByteArrayAsync(timeout.Token);
            return new HttpOutcome.Response((int)response.StatusCode, body);
        }
        catch (Exception error)
        {
            // A dropped connection or a timeout is the common case; the UI
            // shows the shared caption instead of a raw socket message.
            return new HttpOutcome.Transport(
                error is HttpRequestException or OperationCanceledException
                    ? L10n.Tr("network drop")
                    : error.Message);
        }
    }

    // MARK: - Token refresh

    private abstract record RefreshResult
    {
        public sealed record Refreshed(GeminiOAuthCreds Creds) : RefreshResult;
        public sealed record NoClientCredentials : RefreshResult;
        public sealed record Failed : RefreshResult;
    }

    /// Exchange the refresh token at Google's token endpoint. Success is
    /// defined as "the writeback landed" — the CLI reads the same file, and a
    /// rotated token we fail to persist is a token nobody can use again.
    private static async Task<RefreshResult> Refresh(
        GeminiOAuthCreds creds, string credsPath, CancellationToken ct)
    {
        if (creds.RefreshToken is not { } refreshToken) return new RefreshResult.Failed();
        if (GeminiClientExtractor.Resolve() is not { } client) return new RefreshResult.NoClientCredentials();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(RefreshTimeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, TokenUrl);
            request.Headers.TryAddWithoutValidation("Accept", "application/json");
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = client.ClientId,
                ["client_secret"] = client.ClientSecret,
                ["refresh_token"] = refreshToken,
                ["grant_type"] = "refresh_token",
            });
            using var response = await Http.Client.SendAsync(request, timeout.Token);
            if ((int)response.StatusCode != 200) return new RefreshResult.Failed();
            var body = await response.Content.ReadAsByteArrayAsync(timeout.Token);
            if (GeminiCredentials.ApplyRefreshResponse(body, credsPath) is not { } updated)
            {
                return new RefreshResult.Failed();
            }
            return new RefreshResult.Refreshed(updated);
        }
        catch
        {
            return new RefreshResult.Failed();
        }
    }
}

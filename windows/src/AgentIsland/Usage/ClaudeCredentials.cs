using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AgentIsland.Core;

namespace AgentIsland.Usage;

/// Deep module owning Claude OAuth credential acquisition: the
/// env -&gt; credentials-file -&gt; refresh -&gt; rotation-writeback flow, plus the
/// in-app re-auth helpers. On macOS the store is the login Keychain; on
/// Windows, Claude Code persists the same claudeAiOauth JSON at
/// %USERPROFILE%\.claude\.credentials.json.
///
/// The asymmetry between token sources is load-bearing:
///   - An env-token scope-insufficient (403) does NOT short-circuit; we
///     still try the stored token.
///   - A stored-token (or refreshed-token) scope-insufficient short-circuits
///     to re-auth, because refresh re-issues the same scope set and cannot
///     recover a missing `user:profile`.
public static class ClaudeCredentials
{
    /// The UI layer matches on this exact string to swap the error caption
    /// for an in-app re-auth button.
    public const string ReauthRequiredMessage = "re-login: claude /login";

    public const string AuthRequiredMessage = "auth required — run claude";

    public static bool IsAuthRecoverableError(string? message) =>
        message is ReauthRequiredMessage or AuthRequiredMessage;

    private const string RefreshClientId = "9d1c250a-e61b-44d9-88ed-5944d1962f5e";

    public abstract record ProbeOutcome
    {
        public sealed record Success(AppUsage Usage) : ProbeOutcome;
        public sealed record RateLimited : ProbeOutcome;
        public sealed record Unauthorized : ProbeOutcome;
        /// Token is structurally valid but missing a scope the server now
        /// requires (`user:profile`, added mid-2026). Refresh won't help.
        public sealed record ScopeInsufficient : ProbeOutcome;
        public sealed record OtherError(string Message) : ProbeOutcome;
    }

    public abstract record Resolution
    {
        public sealed record Usage(AppUsage Value) : Resolution;
        public sealed record ReauthRequired(string Message) : Resolution;
        public sealed record Failed(string Message) : Resolution;
    }

    // MARK: - Resolution

    /// Token sources, in order of freshness:
    ///   1. CLAUDE_CODE_OAUTH_TOKEN — set by Claude Desktop for child
    ///      processes; always fresh while Desktop is running.
    ///   2. ~/.claude/.credentials.json — written by `claude /login`; the
    ///      access token expires after ~8h, after which we refresh.
    ///   3. platform.claude.com/v1/oauth/token refresh — Anthropic rotates
    ///      the refresh_token on every call; the new pair MUST be persisted
    ///      back or Claude Code itself 401s on its next refresh.
    public static async Task<Resolution> ResolveUsage(Func<string, string?, Task<ProbeOutcome>> probe)
    {
        var lastError = AuthRequiredMessage;
        var cachedCreds = ReadClaudeCreds();
        var plan = cachedCreds?.SubscriptionType;

        var envToken = Environment.GetEnvironmentVariable("CLAUDE_CODE_OAUTH_TOKEN");
        if (!string.IsNullOrEmpty(envToken))
        {
            switch (await probe(envToken, plan))
            {
                case ProbeOutcome.Success success: return new Resolution.Usage(success.Usage);
                case ProbeOutcome.RateLimited: lastError = "rate limited"; break;
                case ProbeOutcome.Unauthorized: break;
                case ProbeOutcome.ScopeInsufficient: lastError = ReauthRequiredMessage; break;
                case ProbeOutcome.OtherError error: lastError = error.Message; break;
            }
        }

        if (cachedCreds is { } creds)
        {
            switch (await probe(creds.AccessToken, plan))
            {
                case ProbeOutcome.Success success: return new Resolution.Usage(success.Usage);
                // A 429 is rate limiting, not an expired token — refreshing
                // wouldn't help and only doubles load on an already-limited
                // endpoint (and needlessly rotates the refresh token).
                case ProbeOutcome.RateLimited: return new Resolution.Failed("rate limited");
                // Refresh hands back tokens with the same scope set, so it
                // cannot recover from a missing-scope 403.
                case ProbeOutcome.ScopeInsufficient: return new Resolution.ReauthRequired(ReauthRequiredMessage);
                case ProbeOutcome.OtherError error: lastError = error.Message; break;
                case ProbeOutcome.Unauthorized:
                    // Only a 401 means the access token expired; refresh it.
                    if (await RefreshClaudeToken(creds.RefreshToken) is { } refreshed)
                    {
                        // Anthropic rotates the refresh token, invalidating the
                        // pair we just used. If we can't persist the rotated
                        // tokens, the on-disk creds are now dead — force a
                        // re-login instead of 401-ing forever.
                        if (!WriteClaudeCreds(creds, refreshed))
                        {
                            return new Resolution.ReauthRequired(ReauthRequiredMessage);
                        }
                        switch (await probe(refreshed.AccessToken, plan))
                        {
                            case ProbeOutcome.Success success: return new Resolution.Usage(success.Usage);
                            case ProbeOutcome.RateLimited: return new Resolution.Failed("rate limited");
                            case ProbeOutcome.ScopeInsufficient: return new Resolution.ReauthRequired(ReauthRequiredMessage);
                            case ProbeOutcome.OtherError error2: lastError = error2.Message; break;
                            case ProbeOutcome.Unauthorized: break;
                        }
                    }
                    break;
            }
        }

        return new Resolution.Failed(lastError);
    }

    // MARK: - Credentials file

    private sealed record ClaudeCreds(
        string AccessToken,
        string RefreshToken,
        string? SubscriptionType);

    private static ClaudeCreds? ReadClaudeCreds()
    {
        try
        {
            var path = IslandPaths.ClaudeCredentialsFile;
            if (!File.Exists(path)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
            if (Jsonl.GetObject(doc.RootElement, "claudeAiOauth") is not { } oauth) return null;
            var access = Jsonl.GetString(oauth, "accessToken");
            var refresh = Jsonl.GetString(oauth, "refreshToken");
            if (string.IsNullOrEmpty(access) || string.IsNullOrEmpty(refresh)) return null;
            return new ClaudeCreds(access!, refresh!, Jsonl.GetString(oauth, "subscriptionType"));
        }
        catch
        {
            return null;
        }
    }

    /// Updates the claudeAiOauth block in place, preserving unrelated fields
    /// (scopes, subscriptionType, rateLimitTier) and any other top-level
    /// keys in the file. Best-effort: a failure means the next refresh pays
    /// the rotation cost again.
    /// Persists the rotated tokens. Returns false if the write ultimately
    /// fails — Anthropic has already invalidated the OLD refresh token
    /// server-side, so a lost write means the on-disk credentials are dead
    /// and the caller must force a re-login rather than 401 forever.
    private static bool WriteClaudeCreds(ClaudeCreds current, RefreshedTokens refreshed)
    {
        try
        {
            var path = IslandPaths.ClaudeCredentialsFile;
            JsonNode root;
            try
            {
                root = JsonNode.Parse(File.ReadAllText(path)) ?? new JsonObject();
            }
            catch
            {
                root = new JsonObject();
            }
            if (root["claudeAiOauth"] is not JsonObject oauth)
            {
                oauth = new JsonObject();
                root["claudeAiOauth"] = oauth;
            }
            oauth["accessToken"] = refreshed.AccessToken;
            oauth["refreshToken"] = refreshed.RefreshToken;
            oauth["expiresAt"] = refreshed.ExpiresAtMs;

            var tmp = path + ".tmp";
            File.WriteAllText(tmp, root.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
            // Retry the rename: Claude Code / Claude Desktop may hold a brief
            // read lock on the creds file, and dropping the rotated token
            // bricks auth for every consumer.
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    File.Move(tmp, path, overwrite: true);
                    return true;
                }
                catch (IOException) when (attempt < 4)
                {
                    System.Threading.Thread.Sleep(40 * (attempt + 1));
                }
            }
        }
        catch (Exception error)
        {
            System.Diagnostics.Debug.WriteLine($"AgentIsland: failed to write rotated Claude tokens: {error.Message}");
            return false;
        }
    }

    /// Cheap local change signal used by the re-auth poll — file mtime here,
    /// keychain `mdat` on macOS.
    public static string? CredentialsModificationStamp()
    {
        try
        {
            var path = IslandPaths.ClaudeCredentialsFile;
            if (!File.Exists(path)) return null;
            return File.GetLastWriteTimeUtc(path).Ticks.ToString();
        }
        catch
        {
            return null;
        }
    }

    // MARK: - Refresh

    private sealed record RefreshedTokens(string AccessToken, string RefreshToken, long ExpiresAtMs);

    private static async Task<RefreshedTokens?> RefreshClaudeToken(string refreshToken)
    {
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post, "https://platform.claude.com/v1/oauth/token");
            var body = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
                ["client_id"] = RefreshClientId,
            });
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            using var response = await Http.Client.SendAsync(request);
            if ((int)response.StatusCode != 200) return null;
            using var doc = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
            var access = Jsonl.GetString(doc.RootElement, "access_token");
            var refresh = Jsonl.GetString(doc.RootElement, "refresh_token");
            if (string.IsNullOrEmpty(access) || string.IsNullOrEmpty(refresh)) return null;
            var expiresIn = Jsonl.GetDouble(doc.RootElement, "expires_in") ?? 28_800;
            var expiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn).ToUnixTimeMilliseconds();
            return new RefreshedTokens(access!, refresh!, expiresAt);
        }
        catch
        {
            return null;
        }
    }

    // MARK: - In-app re-auth

    public static bool CanPromptReauth() => Trigger.CLILocator.Locate("claude") is not null;

    /// Opens a visible terminal running `claude auth login` — the CLI may
    /// need an interactive TTY or print a browser URL, so a hidden process
    /// would strand the user.
    public static bool SpawnReauth()
    {
        if (Trigger.CLILocator.Locate("claude") is not { } cli) return false;
        return TerminalLauncher.RunVisible(cli, "auth login", "Agent Island — Claude login");
    }
}

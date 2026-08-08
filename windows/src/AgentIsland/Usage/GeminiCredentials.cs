using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AgentIsland.Core;

namespace AgentIsland.Usage;

/// How the Gemini CLI is signed in on this machine. Only the personal-OAuth
/// path (`oauth-personal`) exposes the Code Assist quota API this app reads;
/// api-key / vertex-ai logins have no quota endpoint we can speak to, and the
/// UI must say so rather than spin as if data were coming.
public abstract record GeminiAuthDetection
{
    /// No usable ~\.gemini footprint — zero-intrusion, show nothing.
    public sealed record NotInstalled : GeminiAuthDetection;

    /// settings.json declares a non-OAuth auth type (api-key, vertex-ai…).
    public sealed record UnsupportedAuth(string AuthType) : GeminiAuthDetection;

    /// oauth_creds.json present on the oauth-personal path.
    public sealed record OauthPersonal : GeminiAuthDetection;
}

/// Parsed `%USERPROFILE%\.gemini\oauth_creds.json`. `expiry_date` is epoch
/// milliseconds (the CLI writes `Date.now() + expires_in * 1000`).
public sealed record GeminiOAuthCreds(
    string AccessToken,
    string? RefreshToken,
    string? IdToken,
    DateTimeOffset? ExpiryDate)
{
    public string? Email => IdToken is { } token ? GeminiJwt.Email(token) : null;
}

/// Reader/refresher for the Gemini CLI credential + settings files. Refresh
/// results MUST be written back (the CLI reads the same file), writes are
/// atomic (temp file in the same directory, then a replacing move), and every
/// field this app doesn't understand is preserved.
public static class GeminiCredentials
{
    public static string HomeDirectory => Path.Combine(IslandPaths.Home, ".gemini");

    public static string SettingsFile => Path.Combine(HomeDirectory, "settings.json");

    public static string CredsFile => Path.Combine(HomeDirectory, "oauth_creds.json");

    /// The gemini-cli tolerates comments and trailing commas in its own
    /// settings file, so a hand-edited settings.json must not read as "no
    /// auth type declared" — that would silently downgrade an api-key login
    /// to "oauth-personal" and imply live quota we cannot fetch.
    private static readonly JsonDocumentOptions SettingsJsonOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    /// The configured auth type, or null when settings.json is missing or
    /// silent about it (the CLI defaults to oauth-personal in that case).
    /// Accepts the three spellings the CLI has shipped: top-level
    /// `selectedAuthType` (classic), top-level `authType`, and the nested
    /// `security.auth.selectedType` (current).
    public static string? AuthTypeFromSettings(byte[] data)
    {
        try
        {
            using var doc = JsonDocument.Parse(data, SettingsJsonOptions);
            var root = doc.RootElement;
            if (NonEmpty(Jsonl.GetString(root, "selectedAuthType")) is { } classic) return classic;
            if (NonEmpty(Jsonl.GetString(root, "authType")) is { } direct) return direct;
            if (Jsonl.GetObject(root, "security") is not { } security) return null;
            if (Jsonl.GetObject(security, "auth") is not { } auth) return null;
            return NonEmpty(Jsonl.GetString(auth, "selectedType"));
        }
        catch
        {
            return null;
        }
    }

    public static GeminiAuthDetection Detect()
    {
        try
        {
            if (!Directory.Exists(HomeDirectory)) return new GeminiAuthDetection.NotInstalled();
            var settings = SettingsFile;
            if (File.Exists(settings)
                && AuthTypeFromSettings(ReadShared(settings)) is { } type
                && type != "oauth-personal")
            {
                return new GeminiAuthDetection.UnsupportedAuth(type);
            }
            // A bare ~\.gemini from an aborted install has nothing to show.
            if (!File.Exists(CredsFile)) return new GeminiAuthDetection.NotInstalled();
            return new GeminiAuthDetection.OauthPersonal();
        }
        catch
        {
            return new GeminiAuthDetection.NotInstalled();
        }
    }

    public static GeminiOAuthCreds? LoadCreds(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var stream = OpenShared(path);
            using var doc = JsonDocument.Parse(stream);
            var root = doc.RootElement;
            if (NonEmpty(Jsonl.GetString(root, "access_token")) is not { } accessToken) return null;
            return new GeminiOAuthCreds(
                accessToken,
                NonEmpty(Jsonl.GetString(root, "refresh_token")),
                NonEmpty(Jsonl.GetString(root, "id_token")),
                ParseExpiry(root, "expiry_date"));
        }
        catch
        {
            return null;
        }
    }

    /// Refresh a minute early so an in-flight request never races the clock.
    public static bool NeedsRefresh(GeminiOAuthCreds creds, DateTimeOffset? now = null, double skewSeconds = 60)
    {
        if (creds.ExpiryDate is not { } expiry) return false;
        return (now ?? DateTimeOffset.Now).AddSeconds(skewSeconds) >= expiry;
    }

    /// Apply a Google token response (`access_token` / `expires_in` seconds,
    /// occasionally a fresh `id_token`) to oauth_creds.json and persist.
    /// Returns the updated creds, or null when nothing was written — the
    /// caller keeps using its in-memory token.
    public static GeminiOAuthCreds? ApplyRefreshResponse(byte[] data, string path, DateTimeOffset? now = null)
    {
        try
        {
            using var doc = JsonDocument.Parse(data);
            var response = doc.RootElement;
            if (NonEmpty(Jsonl.GetString(response, "access_token")) is not { } accessToken) return null;

            if (!File.Exists(path)) return null;
            JsonObject root;
            using (var existing = OpenShared(path))
            {
                if (JsonNode.Parse(existing) is not JsonObject parsed) return null;
                root = parsed;
            }

            // Track the surviving values in locals: reading them back out of
            // the nodes we just assigned would depend on JsonValue's CLR-type
            // round-tripping rules, which differ from the parsed-file case.
            var refreshToken = NodeString(root["refresh_token"]);
            var idToken = NodeString(root["id_token"]);
            var expiry = ParseExpiry(root["expiry_date"]);

            root["access_token"] = accessToken;
            if (Seconds(response, "expires_in") is { } expiresIn && expiresIn > 0)
            {
                expiry = (now ?? DateTimeOffset.Now).AddSeconds(expiresIn);
                root["expiry_date"] = expiry.Value.ToUnixTimeMilliseconds();
            }
            if (NonEmpty(Jsonl.GetString(response, "refresh_token")) is { } rotated)
            {
                refreshToken = rotated;
                root["refresh_token"] = rotated;
            }
            if (NonEmpty(Jsonl.GetString(response, "id_token")) is { } freshIdToken)
            {
                idToken = freshIdToken;
                root["id_token"] = freshIdToken;
            }

            if (!WriteAtomically(path, root.ToJsonString(WriteOptions))) return null;
            return new GeminiOAuthCreds(accessToken, refreshToken, idToken, expiry);
        }
        catch
        {
            return null;
        }
    }

    /// The gemini CLI keeps oauth_creds.json open while it runs; the default
    /// File.ReadAll* share mode throws against that, and a failed read here
    /// reads to the caller as "never signed in" — or, in the writeback path,
    /// silently drops the rotated refresh token Google already invalidated.
    private static FileStream OpenShared(string path) => new(
        path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

    private static byte[] ReadShared(string path)
    {
        using var stream = OpenShared(path);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    /// Temp file beside the target, then a replacing move. Windows has no
    /// posix mode bits to set — the file inherits the ACL of %USERPROFILE%,
    /// which is already per-user. The move is retried because the CLI may
    /// hold a brief read lock, and a dropped writeback strands a rotated
    /// refresh token that Google has already invalidated server-side.
    private static bool WriteAtomically(string path, string contents)
    {
        var tmp = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(tmp, contents);
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
            System.Diagnostics.Debug.WriteLine($"AgentIsland: failed to write Gemini creds: {error.Message}");
            try { File.Delete(tmp); } catch { }
            return false;
        }
    }

    /// `expiry_date` is epoch milliseconds on current CLI logins; tolerate
    /// epoch seconds from other writer versions.
    public static DateTimeOffset? ParseExpiry(double raw)
    {
        try
        {
            return raw > 1e11
                ? DateTimeOffset.FromUnixTimeMilliseconds((long)raw)
                : DateTimeOffset.FromUnixTimeSeconds((long)raw);
        }
        catch
        {
            return null;
        }
    }

    private static DateTimeOffset? ParseExpiry(JsonElement root, string property)
    {
        if (root.ValueKind != JsonValueKind.Object) return null;
        if (!root.TryGetProperty(property, out var value)) return null;
        if (value.ValueKind != JsonValueKind.Number) return null;
        return value.TryGetDouble(out var raw) ? ParseExpiry(raw) : null;
    }

    private static DateTimeOffset? ParseExpiry(JsonNode? node)
    {
        if (node is not JsonValue value) return null;
        return value.TryGetValue<double>(out var raw) ? ParseExpiry(raw) : null;
    }

    private static string? NodeString(JsonNode? node)
    {
        if (node is not JsonValue value) return null;
        return value.TryGetValue<string>(out var text) ? NonEmpty(text) : null;
    }

    private static double? Seconds(JsonElement root, string property)
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

/// Minimal JWT payload reader — enough to pull the account email out of the
/// Google `id_token` without any signature verification (we only display it).
public static class GeminiJwt
{
    private static JsonDocument? Payload(string jwt)
    {
        var segments = jwt.Split('.');
        if (segments.Length < 2) return null;
        var base64 = segments[1].Replace('-', '+').Replace('_', '/');
        switch (base64.Length % 4)
        {
            case 2: base64 += "=="; break;
            case 3: base64 += "="; break;
            // A one-over remainder is not a valid base64url payload.
            case 1: return null;
        }
        try
        {
            return JsonDocument.Parse(Convert.FromBase64String(base64));
        }
        catch
        {
            return null;
        }
    }

    public static string? Email(string idToken)
    {
        using var payload = Payload(idToken);
        if (payload is null) return null;
        var email = Jsonl.GetString(payload.RootElement, "email");
        return string.IsNullOrEmpty(email) ? null : email;
    }
}

/// Google OAuth client id/secret for the token-refresh call. Google ships
/// them inside the Gemini CLI itself (`oauth2.js`), so we extract them from
/// the local install at runtime instead of hardcoding another product's
/// credentials into this binary.
public sealed record GeminiClientCredentials(string ClientId, string ClientSecret);

public static class GeminiClientExtractor
{
    public const string ClientIdEnvKey = "GEMINI_OAUTH_CLIENT_ID";
    public const string ClientSecretEnvKey = "GEMINI_OAUTH_CLIENT_SECRET";

    private static readonly string[] BinaryEnvKeys = { "GEMINI_PATH", "GEMINI_CLI_PATH" };

    private const string PackageScope = "@google";
    private const string PackageName = "gemini-cli";

    /// Declared before RelativeLayouts: that initializer reads this one, and
    /// static field initializers run in declaration order.
    private static readonly string CoreRelativePath = Path.Combine(
        "node_modules", PackageScope, "gemini-cli-core", "dist", "src", "code_assist", "oauth2.js");

    /// The layouts npm has shipped, walking up from wherever the `gemini`
    /// shim landed: hoisted core, the nested package under a node_modules
    /// root, the scope directory itself, and the unix-style prefix that
    /// MSYS/Git-Bash npm installs still use.
    private static readonly string[] RelativeLayouts =
    {
        CoreRelativePath,
        Path.Combine("node_modules", PackageScope, PackageName, CoreRelativePath),
        Path.Combine(PackageScope, PackageName, CoreRelativePath),
        Path.Combine("lib", "node_modules", PackageScope, PackageName, CoreRelativePath),
    };

    private static readonly Regex ClientIdPattern = new(
        @"OAUTH_CLIENT_ID\s*=\s*[""']([^""']+)[""']", RegexOptions.None, TimeSpan.FromSeconds(2));

    private static readonly Regex ClientSecretPattern = new(
        @"OAUTH_CLIENT_SECRET\s*=\s*[""']([^""']+)[""']", RegexOptions.None, TimeSpan.FromSeconds(2));

    /// Regex pull of `OAUTH_CLIENT_ID` / `OAUTH_CLIENT_SECRET` from the CLI's
    /// oauth2.js (or any bundle chunk that inlines the same constants).
    public static GeminiClientCredentials? Extract(string content)
    {
        if (FirstMatch(ClientIdPattern, content) is not { } id) return null;
        if (FirstMatch(ClientSecretPattern, content) is not { } secret) return null;
        return new GeminiClientCredentials(id, secret);
    }

    public static GeminiClientCredentials? FromEnvironment()
    {
        var id = Environment.GetEnvironmentVariable(ClientIdEnvKey)?.Trim();
        var secret = Environment.GetEnvironmentVariable(ClientSecretEnvKey)?.Trim();
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(secret)) return null;
        return new GeminiClientCredentials(id, secret);
    }

    /// Env override first, then the local gemini-cli install.
    public static GeminiClientCredentials? Resolve()
    {
        if (FromEnvironment() is { } fromEnv) return fromEnv;
        if (LocateOAuth2Js() is not { } path) return null;
        try
        {
            return Extract(File.ReadAllText(path));
        }
        catch
        {
            return null;
        }
    }

    /// Walk up from the resolved `gemini` shim looking for the CLI package,
    /// then fall back to the fixed package roots for setups where the shim
    /// itself isn't findable.
    public static string? LocateOAuth2Js()
    {
        try
        {
            if (LocateGeminiBinary() is { } binary && OAuth2JsNearBinary(binary) is { } near) return near;
            foreach (var root in PackageRoots())
            {
                var candidate = Path.Combine(root, CoreRelativePath);
                if (File.Exists(candidate)) return candidate;
            }
        }
        catch
        {
        }
        return null;
    }

    /// The env overrides, then the shared CLI locator — it already probes
    /// every npm/pnpm/Volta/nvm shim directory plus PATH.
    public static string? LocateGeminiBinary()
    {
        foreach (var key in BinaryEnvKeys)
        {
            var raw = Environment.GetEnvironmentVariable(key);
            if (string.IsNullOrWhiteSpace(raw)) continue;
            if (File.Exists(raw)) return raw;
        }
        return Trigger.CLILocator.Locate("gemini");
    }

    private static string? OAuth2JsNearBinary(string binary)
    {
        string? dir;
        try
        {
            dir = Path.GetDirectoryName(Path.GetFullPath(binary));
        }
        catch
        {
            return null;
        }
        for (var depth = 0; depth < 8 && !string.IsNullOrEmpty(dir); depth++)
        {
            foreach (var relative in RelativeLayouts)
            {
                var candidate = Path.Combine(dir, relative);
                if (File.Exists(candidate)) return candidate;
            }
            var parent = Path.GetDirectoryName(dir);
            if (parent is null || parent == dir) break;
            dir = parent;
        }
        return null;
    }

    private static IEnumerable<string> PackageRoots()
    {
        var roots = new List<string>
        {
            // npm's default global prefix on Windows.
            Path.Combine(IslandPaths.RoamingAppData, "npm", "node_modules", PackageScope, PackageName),
        };

        foreach (var variable in new[] { "npm_config_prefix", "NPM_CONFIG_PREFIX" })
        {
            var prefix = Environment.GetEnvironmentVariable(variable);
            if (string.IsNullOrWhiteSpace(prefix)) continue;
            roots.Add(Path.Combine(prefix, "node_modules", PackageScope, PackageName));
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrEmpty(programFiles))
        {
            roots.Add(Path.Combine(programFiles, "nodejs", "node_modules", PackageScope, PackageName));
        }

        // pnpm's global installs sit under a store-version directory that the
        // shim directory does not contain, so walking up from the shim can
        // never reach them.
        var pnpmGlobal = Path.Combine(IslandPaths.LocalAppData, "pnpm", "global");
        try
        {
            if (Directory.Exists(pnpmGlobal))
            {
                foreach (var version in Directory.EnumerateDirectories(pnpmGlobal))
                {
                    roots.Add(Path.Combine(version, "node_modules", PackageScope, PackageName));
                }
            }
        }
        catch
        {
        }

        roots.Add(Path.Combine(IslandPaths.Home, ".local", "lib", "node_modules", PackageScope, PackageName));
        return roots;
    }

    private static string? FirstMatch(Regex pattern, string content)
    {
        try
        {
            var match = pattern.Match(content);
            if (!match.Success || match.Groups.Count < 2) return null;
            var value = match.Groups[1].Value;
            return value.Length == 0 ? null : value;
        }
        catch (RegexMatchTimeoutException)
        {
            return null;
        }
    }
}

import Foundation

/// How the Gemini CLI is signed in on this machine. Only the personal-OAuth
/// path (`oauth-personal`) exposes the Code Assist quota API this app reads;
/// api-key / vertex-ai logins have no quota endpoint we can speak to.
enum GeminiAuthDetection: Equatable {
    /// No usable ~/.gemini footprint — zero-intrusion, show nothing.
    case notInstalled
    /// settings.json declares a non-OAuth auth type (api-key, vertex-ai…).
    case unsupportedAuth(String)
    /// oauth_creds.json present on the oauth-personal path.
    case oauthPersonal
}

/// Parsed `~/.gemini/oauth_creds.json`. `expiry_date` is epoch milliseconds
/// (the CLI writes `Date.now() + expires_in * 1000`).
struct GeminiOAuthCreds: Equatable {
    let accessToken: String
    let refreshToken: String?
    let idToken: String?
    let expiryDate: Date?

    var email: String? { idToken.flatMap(GeminiJWT.email(fromIDToken:)) }
}

/// Reader/refresher for the Gemini CLI credential + settings files. Mirrors
/// `GrokAuthFile`'s contract: refresh results MUST be written back (the CLI
/// reads the same file), writes are atomic (tmp file created 0600 in the
/// same directory, then rename(2)), and every field this app doesn't
/// understand is preserved.
enum GeminiCredentials {
    static func homeDirectory() -> URL {
        let home = NSString("~/.gemini").expandingTildeInPath
        return URL(fileURLWithPath: home, isDirectory: true)
    }

    static func settingsURL(home: URL = homeDirectory()) -> URL {
        home.appendingPathComponent("settings.json")
    }

    static func credsURL(home: URL = homeDirectory()) -> URL {
        home.appendingPathComponent("oauth_creds.json")
    }

    /// The configured auth type, or nil when settings.json is missing or
    /// silent about it (the CLI defaults to oauth-personal in that case).
    /// Accepts the three spellings the CLI has shipped: top-level
    /// `selectedAuthType` (classic), top-level `authType`, and the nested
    /// `security.auth.selectedType` (current).
    static func authType(fromSettings data: Data) -> String? {
        guard let root = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else {
            return nil
        }
        if let direct = nonEmpty(root["selectedAuthType"]) { return direct }
        if let direct = nonEmpty(root["authType"]) { return direct }
        guard let security = root["security"] as? [String: Any],
              let auth = security["auth"] as? [String: Any] else { return nil }
        return nonEmpty(auth["selectedType"])
    }

    static func detect(home: URL = homeDirectory()) -> GeminiAuthDetection {
        var isDir: ObjCBool = false
        guard FileManager.default.fileExists(atPath: home.path, isDirectory: &isDir),
              isDir.boolValue else { return .notInstalled }
        if let data = try? Data(contentsOf: settingsURL(home: home)),
           let type = authType(fromSettings: data),
           type != "oauth-personal" {
            return .unsupportedAuth(type)
        }
        // A bare ~/.gemini from an aborted install has nothing to show.
        guard FileManager.default.fileExists(atPath: credsURL(home: home).path) else {
            return .notInstalled
        }
        return .oauthPersonal
    }

    static func loadCreds(from url: URL) -> GeminiOAuthCreds? {
        guard let data = try? Data(contentsOf: url),
              let root = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              let accessToken = nonEmpty(root["access_token"]) else { return nil }
        return GeminiOAuthCreds(
            accessToken: accessToken,
            refreshToken: nonEmpty(root["refresh_token"]),
            idToken: nonEmpty(root["id_token"]),
            expiryDate: parseExpiry(root["expiry_date"])
        )
    }

    /// Refresh a minute early so an in-flight request never races the clock.
    static func needsRefresh(_ creds: GeminiOAuthCreds,
                             now: Date = Date(),
                             skew: TimeInterval = 60) -> Bool {
        guard let expiryDate = creds.expiryDate else { return false }
        return now.addingTimeInterval(skew) >= expiryDate
    }

    /// Apply a Google token response (`access_token` / `expires_in` seconds,
    /// occasionally a fresh `id_token`) to oauth_creds.json and persist.
    /// Returns the updated creds, or nil when nothing was written — the
    /// caller keeps using its in-memory token.
    @discardableResult
    static func applyRefreshResponse(_ data: Data,
                                     to url: URL,
                                     now: Date = Date()) -> GeminiOAuthCreds? {
        guard let response = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              let accessToken = nonEmpty(response["access_token"]) else { return nil }

        guard let fileData = try? Data(contentsOf: url),
              var root = try? JSONSerialization.jsonObject(with: fileData) as? [String: Any] else {
            return nil
        }

        root["access_token"] = accessToken
        if let expiresIn = seconds(response["expires_in"]), expiresIn > 0 {
            root["expiry_date"] = Int((now.timeIntervalSince1970 + expiresIn) * 1000)
        }
        if let rotatedRefresh = nonEmpty(response["refresh_token"]) {
            root["refresh_token"] = rotatedRefresh
        }
        if let freshIDToken = nonEmpty(response["id_token"]) {
            root["id_token"] = freshIDToken
        }

        guard let output = try? JSONSerialization.data(
            withJSONObject: root, options: [.prettyPrinted, .sortedKeys]
        ) else { return nil }

        let tmp = url.deletingLastPathComponent()
            .appendingPathComponent(".oauth_creds.json.tmp-\(UUID().uuidString)")
        guard FileManager.default.createFile(
            atPath: tmp.path, contents: output,
            attributes: [.posixPermissions: 0o600]
        ) else { return nil }
        guard rename(tmp.path, url.path) == 0 else {
            try? FileManager.default.removeItem(at: tmp)
            return nil
        }
        return GeminiOAuthCreds(
            accessToken: accessToken,
            refreshToken: nonEmpty(root["refresh_token"]),
            idToken: nonEmpty(root["id_token"]),
            expiryDate: parseExpiry(root["expiry_date"])
        )
    }

    /// `expiry_date` is epoch milliseconds on current CLI logins; tolerate
    /// epoch seconds from other writer versions.
    static func parseExpiry(_ value: Any?) -> Date? {
        if let raw = value as? Double {
            return Date(timeIntervalSince1970: raw > 1e11 ? raw / 1000 : raw)
        }
        if let raw = value as? Int {
            return parseExpiry(Double(raw))
        }
        return nil
    }

    private static func seconds(_ value: Any?) -> TimeInterval? {
        if let raw = value as? Double { return raw }
        if let raw = value as? Int { return TimeInterval(raw) }
        if let raw = value as? String { return TimeInterval(raw) }
        return nil
    }

    private static func nonEmpty(_ value: Any?) -> String? {
        guard let raw = value as? String, !raw.isEmpty else { return nil }
        return raw
    }
}

/// Minimal JWT payload reader — enough to pull the account email out of the
/// Google `id_token` without any signature verification (we only display it).
enum GeminiJWT {
    static func payload(fromJWT jwt: String) -> [String: Any]? {
        let segments = jwt.split(separator: ".")
        guard segments.count >= 2 else { return nil }
        var base64 = String(segments[1])
            .replacingOccurrences(of: "-", with: "+")
            .replacingOccurrences(of: "_", with: "/")
        while base64.count % 4 != 0 { base64.append("=") }
        guard let data = Data(base64Encoded: base64),
              let object = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else {
            return nil
        }
        return object
    }

    static func email(fromIDToken idToken: String) -> String? {
        guard let email = payload(fromJWT: idToken)?["email"] as? String,
              !email.isEmpty else { return nil }
        return email
    }
}

/// Google OAuth client id/secret for the token-refresh call. Google ships
/// them inside the Gemini CLI itself (`oauth2.js`), so we extract them from
/// the local install at runtime instead of hardcoding another product's
/// credentials into this binary.
struct GeminiClientCredentials: Equatable {
    let clientID: String
    let clientSecret: String
}

enum GeminiClientExtractor {
    static let clientIDEnvKey = "GEMINI_OAUTH_CLIENT_ID"
    static let clientSecretEnvKey = "GEMINI_OAUTH_CLIENT_SECRET"
    private static let binaryEnvKeys = ["GEMINI_PATH", "GEMINI_CLI_PATH"]
    private static let coreRelativePath =
        "node_modules/@google/gemini-cli-core/dist/src/code_assist/oauth2.js"

    /// Regex pull of `OAUTH_CLIENT_ID` / `OAUTH_CLIENT_SECRET` from the CLI's
    /// oauth2.js (or any bundle chunk that inlines the same constants).
    static func extract(fromOAuth2JS content: String) -> GeminiClientCredentials? {
        guard let id = firstMatch(#"OAUTH_CLIENT_ID\s*=\s*["']([^"']+)["']"#, in: content),
              let secret = firstMatch(#"OAUTH_CLIENT_SECRET\s*=\s*["']([^"']+)["']"#, in: content)
        else { return nil }
        return GeminiClientCredentials(clientID: id, clientSecret: secret)
    }

    static func fromEnvironment(
        _ env: [String: String] = ProcessInfo.processInfo.environment
    ) -> GeminiClientCredentials? {
        guard let id = env[clientIDEnvKey]?.trimmingCharacters(in: .whitespacesAndNewlines),
              let secret = env[clientSecretEnvKey]?.trimmingCharacters(in: .whitespacesAndNewlines),
              !id.isEmpty, !secret.isEmpty else { return nil }
        return GeminiClientCredentials(clientID: id, clientSecret: secret)
    }

    /// Env override first, then the local gemini-cli install.
    static func resolve() -> GeminiClientCredentials? {
        if let fromEnv = fromEnvironment() { return fromEnv }
        guard let jsURL = locateOAuth2JS(),
              let content = try? String(contentsOf: jsURL, encoding: .utf8) else { return nil }
        return extract(fromOAuth2JS: content)
    }

    /// Walk up from the resolved `gemini` binary looking for the CLI package
    /// in its three shipped layouts (direct core install, npm-style nested
    /// package, Homebrew libexec), then fall back to the fixed Homebrew
    /// package roots for setups where the binary itself isn't findable.
    static func locateOAuth2JS(
        env: [String: String] = ProcessInfo.processInfo.environment
    ) -> URL? {
        if let binary = locateGeminiBinary(env: env),
           let near = oauth2JS(nearBinary: binary) {
            return near
        }
        let home = NSString("~").expandingTildeInPath
        let packageRoots = [
            "/opt/homebrew/opt/gemini-cli/libexec/lib/node_modules/@google/gemini-cli",
            "/usr/local/opt/gemini-cli/libexec/lib/node_modules/@google/gemini-cli",
            "\(home)/.local/lib/node_modules/@google/gemini-cli",
        ]
        for root in packageRoots {
            let candidate = URL(fileURLWithPath: root).appendingPathComponent(coreRelativePath)
            if FileManager.default.fileExists(atPath: candidate.path) { return candidate }
        }
        return nil
    }

    static func locateGeminiBinary(
        env: [String: String] = ProcessInfo.processInfo.environment
    ) -> URL? {
        for key in binaryEnvKeys {
            if let raw = env[key], !raw.isEmpty,
               isExecutableFile(raw) {
                return URL(fileURLWithPath: raw)
            }
        }
        if let pathEnv = env["PATH"] {
            for dir in pathEnv.split(separator: ":") where !dir.isEmpty {
                let candidate = "\(dir)/gemini"
                if isExecutableFile(candidate) { return URL(fileURLWithPath: candidate) }
            }
        }
        let home = NSString("~").expandingTildeInPath
        let candidates = [
            "\(home)/.local/bin/gemini",
            "/opt/homebrew/bin/gemini",
            "/usr/local/bin/gemini",
            "/usr/bin/gemini",
        ]
        for candidate in candidates where isExecutableFile(candidate) {
            return URL(fileURLWithPath: candidate)
        }
        return nil
    }

    private static func oauth2JS(nearBinary binary: URL) -> URL? {
        let resolved = binary.resolvingSymlinksInPath()
        var dir = resolved.deletingLastPathComponent()
        for _ in 0..<8 {
            let relatives = [
                coreRelativePath,
                "@google/gemini-cli/\(coreRelativePath)",
                "lib/node_modules/@google/gemini-cli/\(coreRelativePath)",
                "libexec/lib/node_modules/@google/gemini-cli/\(coreRelativePath)",
            ]
            for relative in relatives {
                let candidate = dir.appendingPathComponent(relative)
                if FileManager.default.fileExists(atPath: candidate.path) { return candidate }
            }
            let parent = dir.deletingLastPathComponent()
            if parent.path == dir.path { break }
            dir = parent
        }
        return nil
    }

    private static func isExecutableFile(_ path: String) -> Bool {
        var isDir: ObjCBool = false
        guard FileManager.default.fileExists(atPath: path, isDirectory: &isDir),
              !isDir.boolValue else { return false }
        return FileManager.default.isExecutableFile(atPath: path)
    }

    private static func firstMatch(_ pattern: String, in content: String) -> String? {
        guard let regex = try? NSRegularExpression(pattern: pattern),
              let match = regex.firstMatch(
                in: content,
                range: NSRange(content.startIndex..., in: content)
              ),
              match.numberOfRanges >= 2,
              let range = Range(match.range(at: 1), in: content) else { return nil }
        let value = String(content[range])
        return value.isEmpty ? nil : value
    }
}

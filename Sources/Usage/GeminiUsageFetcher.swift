import Foundation

/// Fetches the Gemini CLI's Code Assist quota buckets, authenticating with
/// `~/.gemini/oauth_creds.json` and refreshing through Google's token
/// endpoint when the access token is expired or rejected. Display-only —
/// no transcript monitoring, no alarms live here.
enum GeminiUsageFetcher {
    enum Outcome {
        case success(GeminiQuotaSnapshot)
        /// Refresh path exhausted — the user has to run `gemini` and re-auth.
        case reauthRequired
        /// A refresh was required but no client id/secret could be extracted
        /// from a local gemini-cli install (and no env override).
        case needsCLIInstall
        /// Google's consumer-tier shutdown verdict — the account moved to
        /// Antigravity. A state, not an error.
        case migratedToAntigravity
        case failed(String)
        /// settings.json declares api-key / vertex-ai.
        case unsupportedAuth(String)
        /// No oauth_creds.json — Gemini CLI never signed in on this machine.
        case notInstalled
    }

    private static let loadCodeAssistURL =
        "https://cloudcode-pa.googleapis.com/v1internal:loadCodeAssist"
    private static let quotaURL =
        "https://cloudcode-pa.googleapis.com/v1internal:retrieveUserQuota"
    private static let tokenURL = "https://oauth2.googleapis.com/token"

    static func fetch() async -> Outcome {
        switch GeminiCredentials.detect() {
        case .notInstalled: return .notInstalled
        case .unsupportedAuth(let type): return .unsupportedAuth(type)
        case .oauthPersonal: break
        }
        let credsURL = GeminiCredentials.credsURL()
        guard var creds = GeminiCredentials.loadCreds(from: credsURL) else {
            return .notInstalled
        }

        // Proactive refresh near expiry (Google access tokens live ~1h).
        if GeminiCredentials.needsRefresh(creds), creds.refreshToken != nil {
            switch await refresh(creds: creds, credsURL: credsURL) {
            case .refreshed(let fresh): creds = fresh
            case .noClientCredentials: return .needsCLIInstall
            // Clock skew can make a working token look expired — still try.
            case .failed: break
            }
        }

        switch await fetchSnapshot(token: creds.accessToken) {
        case .success(let snapshot):
            return .success(snapshot)
        case .migrated:
            return .migratedToAntigravity
        case .unauthorized:
            guard creds.refreshToken != nil else { return .reauthRequired }
            switch await refresh(creds: creds, credsURL: credsURL) {
            case .noClientCredentials: return .needsCLIInstall
            case .failed: return .reauthRequired
            case .refreshed(let fresh):
                switch await fetchSnapshot(token: fresh.accessToken) {
                case .success(let snapshot): return .success(snapshot)
                case .migrated:              return .migratedToAntigravity
                case .unauthorized:          return .reauthRequired
                case .failed(let message):   return .failed(message)
                }
            }
        case .failed(let message):
            return .failed(message)
        }
    }

    // MARK: - Quota

    private enum SnapshotResult {
        case success(GeminiQuotaSnapshot)
        case migrated
        case unauthorized
        case failed(String)
    }

    private static func fetchSnapshot(token: String) async -> SnapshotResult {
        let profileOutcome = await post(
            url: loadCodeAssistURL,
            token: token,
            body: ["metadata": ["ideType": "GEMINI_CLI", "pluginType": "GEMINI"]]
        )

        var profile: GeminiQuotaParser.CodeAssistProfile?
        switch profileOutcome {
        case .http(401, _), .http(403, _):
            return .unauthorized
        case .http(let status, let data):
            if GeminiQuotaParser.isMigrationSignal(data) { return .migrated }
            // The tier/project call is garnish for the quota call — a non-200
            // here still lets the bucket fetch try with an empty project.
            if status == 200 { profile = GeminiQuotaParser.parseLoadCodeAssist(data) }
        case .transport(let message):
            return .failed(message)
        }

        var quotaBody: [String: Any] = [:]
        if let projectID = profile?.projectID { quotaBody["project"] = projectID }
        switch await post(url: quotaURL, token: token, body: quotaBody) {
        case .http(401, _), .http(403, _):
            return .unauthorized
        case .http(let status, let data):
            if GeminiQuotaParser.isMigrationSignal(data) { return .migrated }
            guard status == 200 else { return .failed("http \(status)") }
            guard let buckets = GeminiQuotaParser.parseQuota(data) else {
                return .failed("parse error")
            }
            return .success(GeminiQuotaSnapshot(
                buckets: buckets,
                tierID: profile?.tierID,
                tierLabel: profile?.tierLabel
            ))
        case .transport(let message):
            return .failed(message)
        }
    }

    private enum HTTPOutcome {
        case http(Int, Data)
        case transport(String)
    }

    private static func post(url: String, token: String, body: [String: Any]) async -> HTTPOutcome {
        guard let requestURL = URL(string: url),
              let payload = try? JSONSerialization.data(withJSONObject: body) else {
            return .transport("bad request")
        }
        var req = URLRequest(url: requestURL)
        req.httpMethod = "POST"
        req.timeoutInterval = 25 // never let a wedged tunnel hang the poll loop
        req.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization")
        req.setValue("application/json", forHTTPHeaderField: "Content-Type")
        req.setValue("application/json", forHTTPHeaderField: "Accept")
        req.httpBody = payload
        do {
            let (data, response) = try await URLSession.shared.data(for: req)
            let status = (response as? HTTPURLResponse)?.statusCode ?? 0
            return .http(status, data)
        } catch {
            return .transport(error is URLError ? L10n.tr("network drop") : error.localizedDescription)
        }
    }

    // MARK: - Token refresh

    private enum RefreshResult {
        case refreshed(GeminiOAuthCreds)
        case noClientCredentials
        case failed
    }

    /// Exchange the refresh token at Google's token endpoint. Success is
    /// defined as "the writeback landed" — the CLI reads the same file.
    private static func refresh(creds: GeminiOAuthCreds, credsURL: URL) async -> RefreshResult {
        guard let refreshToken = creds.refreshToken else { return .failed }
        guard let client = GeminiClientExtractor.resolve() else { return .noClientCredentials }
        guard let endpoint = URL(string: tokenURL) else { return .failed }

        var req = URLRequest(url: endpoint)
        req.httpMethod = "POST"
        req.timeoutInterval = 20
        req.setValue("application/x-www-form-urlencoded", forHTTPHeaderField: "Content-Type")
        req.setValue("application/json", forHTTPHeaderField: "Accept")
        req.httpBody = formEncoded([
            ("client_id", client.clientID),
            ("client_secret", client.clientSecret),
            ("refresh_token", refreshToken),
            ("grant_type", "refresh_token"),
        ])

        guard let (data, response) = try? await URLSession.shared.data(for: req),
              (response as? HTTPURLResponse)?.statusCode == 200,
              let updated = GeminiCredentials.applyRefreshResponse(data, to: credsURL) else {
            return .failed
        }
        return .refreshed(updated)
    }

    private static func formEncoded(_ pairs: [(String, String)]) -> Data {
        var allowed = CharacterSet.alphanumerics
        allowed.insert(charactersIn: "-._~")
        let body = pairs.map { key, value in
            let escaped = value.addingPercentEncoding(withAllowedCharacters: allowed) ?? value
            return "\(key)=\(escaped)"
        }.joined(separator: "&")
        return Data(body.utf8)
    }
}

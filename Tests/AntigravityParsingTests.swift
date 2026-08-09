import Foundation

private enum TestFailure: Error, CustomStringConvertible {
    case assertion(String)

    var description: String {
        switch self {
        case .assertion(let message): return message
        }
    }
}

@discardableResult
private func expect(_ condition: @autoclosure () -> Bool, _ message: String) throws -> Bool {
    guard condition() else { throw TestFailure.assertion(message) }
    return true
}

// MARK: - Fixtures

private func base64URL(_ string: String) -> String {
    Data(string.utf8).base64EncodedString()
        .replacingOccurrences(of: "+", with: "-")
        .replacingOccurrences(of: "/", with: "_")
        .replacingOccurrences(of: "=", with: "")
}

/// Structurally real JWT (header.payload.signature), unsigned like the test
/// only needs — the app never verifies signatures on the id_token.
private let fixtureIDToken = [
    base64URL(#"{"alg":"RS256","typ":"JWT"}"#),
    base64URL(#"{"iss":"https://accounts.google.com","email":"tester@gmail.com","hd":null}"#),
    "sig",
].joined(separator: ".")

private func fixtureCredsJSON(expiryMs: Int) -> String {
    """
    {
      "access_token": "old-access-token",
      "refresh_token": "old-refresh-token",
      "scope": "https://www.googleapis.com/auth/cloud-platform openid",
      "token_type": "Bearer",
      "id_token": "\(fixtureIDToken)",
      "expiry_date": \(expiryMs)
    }
    """
}

private func makeTempGeminiHome(
    credsExpiryMs: Int? = 1_785_888_020_774,
    settingsJSON: String? = nil
) throws -> URL {
    let dir = FileManager.default.temporaryDirectory
        .appendingPathComponent("gemini-tests-\(UUID().uuidString)", isDirectory: true)
    try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
    if let credsExpiryMs {
        let url = dir.appendingPathComponent("oauth_creds.json")
        try fixtureCredsJSON(expiryMs: credsExpiryMs).write(to: url, atomically: true, encoding: .utf8)
        try FileManager.default.setAttributes([.posixPermissions: 0o600], ofItemAtPath: url.path)
    }
    if let settingsJSON {
        let url = dir.appendingPathComponent("settings.json")
        try settingsJSON.write(to: url, atomically: true, encoding: .utf8)
    }
    return dir
}

// MARK: - Settings / detection

/// The CLI ships no `oauth_creds.json` — its token lives in the keychain —
/// so a data root alone must read as signed-out, and the keychain item alone
/// (no creds file anywhere) must still read as signed-in. Getting this
/// backwards is exactly what left a freshly signed-in install showing "not
/// detected" (repro, 2026-08-08).
private func testDetectionStates() throws {
    let missing = FileManager.default.temporaryDirectory
        .appendingPathComponent("gemini-tests-missing-\(UUID().uuidString)", isDirectory: true)
    try expect(
        AntigravityCredentials.detect(roots: [missing], keychainCredential: false) == .notInstalled,
        "no data root and no keychain item must read as notInstalled")

    let bare = try makeTempGeminiHome(credsExpiryMs: nil)
    defer { try? FileManager.default.removeItem(at: bare) }
    try expect(
        AntigravityCredentials.detect(roots: [bare], keychainCredential: false) == .signedOut,
        "a CLI data root without any credential must read as signedOut, not notInstalled")
    try expect(
        AntigravityCredentials.detect(roots: [bare], keychainCredential: true) == .signedIn,
        "the keychain item alone must prove sign-in — the CLI writes no creds file")

    let withFile = try makeTempGeminiHome()
    defer { try? FileManager.default.removeItem(at: withFile) }
    try expect(
        AntigravityCredentials.detect(roots: [withFile], keychainCredential: false) == .signedIn,
        "an IDE root carrying oauth_creds.json must detect without the keychain")

    try expect(
        AntigravityCredentials.detect(roots: [], keychainCredential: true) == .signedIn,
        "a credential with no surviving data root still means a signed-in account")

    try expect(
        AntigravityCredentials.detect(roots: [missing, withFile], keychainCredential: false) == .signedIn,
        "one good root among several must be enough")
}

/// The CLI wraps every user message in a `<USER_REQUEST>` envelope and then
/// appends metadata blocks; a raw label would show the tags and the machine's
/// local time. Shapes taken from a real transcript (2026-08-08).
private func testAntigravityRequestTextUnwrapsEnvelope() throws {
    let request = "Write a haiku about gravity, then explain why apples fall."
    let real = "<USER_REQUEST>\n\(request)\n"
        + "</USER_REQUEST>\n<ADDITIONAL_METADATA>\nThe current local time is: 2026-08-08T22:01:06-04:00.\n"
        + "</ADDITIONAL_METADATA>"
    let label = SessionScanner.antigravityRequestText(real)
    try expect(label == String(request.prefix(48)),
               "the envelope and trailing metadata must be stripped, then clipped to 48")
    try expect(!(label ?? "").contains("USER_REQUEST"),
               "no envelope tag may leak into a label")
    try expect(!(label ?? "").contains("local time"),
               "the metadata block must never become the label")

    try expect(SessionScanner.antigravityRequestText("<USER_REQUEST>\n--output-format\n</USER_REQUEST>")
                == "--output-format",
               "a short request must survive intact")
    try expect(SessionScanner.antigravityRequestText("plain text, no envelope") == "plain text, no envelope",
               "a record without the envelope must still yield its text")
    try expect(SessionScanner.antigravityRequestText("<USER_REQUEST>\n\n</USER_REQUEST>") == nil,
               "an empty request must yield nil so the caller falls back")
    try expect(SessionScanner.antigravityRequestText("   \n  ") == nil,
               "whitespace must yield nil, never a blank label")
}

/// The roots are probed under ~/.gemini, and the CLI root is preferred —
/// pointing these at ~/.antigravity is what broke detection.
private func testDataRootNamesAndOrder() throws {
    try expect(AntigravityCredentials.rootNames.first == "antigravity-cli",
               "the CLI root must be probed first — it is the one a brew install creates")
    try expect(Set(AntigravityCredentials.rootNames)
                == ["antigravity-cli", "antigravity-ide", "antigravity"],
               "all three renamed roots must stay in the probe list")
    let home = AntigravityCredentials.homeDirectory().path
    try expect(home.contains("/.gemini/"),
               "roots live under ~/.gemini, never ~/.antigravity")
}

// MARK: - Credentials

private func testLoadCredsParsesFieldsAndEmail() throws {
    let home = try makeTempGeminiHome(credsExpiryMs: 1_785_888_020_774)
    defer { try? FileManager.default.removeItem(at: home) }

    let creds = AntigravityCredentials.loadCreds(from: AntigravityCredentials.credsURL(home: home))
    try expect(creds != nil, "fixture oauth_creds.json must load")
    try expect(creds?.accessToken == "old-access-token", "access_token must parse")
    try expect(creds?.refreshToken == "old-refresh-token", "refresh_token must parse")
    try expect(creds?.email == "tester@gmail.com", "email must decode from the id_token JWT")
    if let expiryDate = creds?.expiryDate {
        try expect(abs(expiryDate.timeIntervalSince1970 - 1_785_888_020.774) < 1,
                   "expiry_date must decode as epoch milliseconds")
    } else {
        try expect(false, "expiry_date must be present")
    }
}

private func testNeedsRefreshHonorsSkew() throws {
    let now = Date(timeIntervalSince1970: 1_000_000)
    func creds(expiringIn seconds: TimeInterval?) -> AntigravityOAuthCreds {
        AntigravityOAuthCreds(
            accessToken: "t",
            refreshToken: "r",
            idToken: nil,
            expiryDate: seconds.map { now.addingTimeInterval($0) }
        )
    }
    try expect(AntigravityCredentials.needsRefresh(creds(expiringIn: 30), now: now),
               "a token expiring inside the skew window must refresh")
    try expect(!AntigravityCredentials.needsRefresh(creds(expiringIn: 600), now: now),
               "a token with 10 minutes left must not refresh")
    try expect(!AntigravityCredentials.needsRefresh(creds(expiringIn: nil), now: now),
               "no expiry means no proactive refresh")
}

private func testApplyRefreshRewritesAtomicallyAndPreservesEverythingElse() throws {
    let home = try makeTempGeminiHome(credsExpiryMs: 1_000)
    defer { try? FileManager.default.removeItem(at: home) }
    let url = AntigravityCredentials.credsURL(home: home)
    let now = Date()
    let response = Data("""
    { "access_token": "new-access-token", "expires_in": 3599,
      "scope": "openid", "token_type": "Bearer" }
    """.utf8)

    let updated = AntigravityCredentials.applyRefreshResponse(response, to: url, now: now)
    try expect(updated != nil, "refresh writeback must succeed")
    try expect(updated?.accessToken == "new-access-token", "returned creds must carry the new token")
    try expect(updated?.refreshToken == "old-refresh-token",
               "an unrotated refresh token must survive the writeback")

    let raw = try Data(contentsOf: url)
    guard let root = try JSONSerialization.jsonObject(with: raw) as? [String: Any] else {
        try expect(false, "rewritten oauth_creds.json must stay a JSON object")
        return
    }
    try expect(root["access_token"] as? String == "new-access-token",
               "access_token must be rewritten on disk")
    try expect(root["refresh_token"] as? String == "old-refresh-token",
               "refresh_token must be preserved on disk")
    try expect(root["scope"] as? String == "https://www.googleapis.com/auth/cloud-platform openid",
               "fields this app doesn't understand must survive")
    if let ms = root["expiry_date"] as? Double {
        let expected = (now.timeIntervalSince1970 + 3599) * 1000
        try expect(abs(ms - expected) < 2_000, "expiry_date must be rewritten as epoch milliseconds")
    } else {
        try expect(false, "expiry_date must remain a number")
    }

    let attrs = try FileManager.default.attributesOfItem(atPath: url.path)
    let perms = (attrs[.posixPermissions] as? NSNumber)?.uint16Value ?? 0
    try expect(perms & 0o777 == 0o600, "rewritten oauth_creds.json must stay 0600")
}

private func testFailedRefreshLeavesFileUntouched() throws {
    let home = try makeTempGeminiHome(credsExpiryMs: 1_000)
    defer { try? FileManager.default.removeItem(at: home) }
    let url = AntigravityCredentials.credsURL(home: home)
    let before = try Data(contentsOf: url)

    let updated = AntigravityCredentials.applyRefreshResponse(
        Data(#"{ "error": "invalid_grant" }"#.utf8), to: url
    )
    try expect(updated == nil, "an error response must not report success")
    let after = try Data(contentsOf: url)
    try expect(before == after, "a failed refresh must leave oauth_creds.json byte-identical")
}

// MARK: - Client extraction

private func testClientExtractionRegex() throws {
    let doubleQuoted = """
    // license header
    export const OAUTH_CLIENT_ID = "681255809395-abc.apps.googleusercontent.com";
    export const OAUTH_CLIENT_SECRET = "GOCSPX-fixture-secret";
    """
    let extracted = GeminiClientExtractor.extract(fromOAuth2JS: doubleQuoted)
    try expect(extracted?.clientID == "681255809395-abc.apps.googleusercontent.com",
               "double-quoted client id must extract")
    try expect(extracted?.clientSecret == "GOCSPX-fixture-secret",
               "double-quoted client secret must extract")

    let minified = "var a=1;const OAUTH_CLIENT_ID='id-single';const OAUTH_CLIENT_SECRET='secret-single';x()"
    let single = GeminiClientExtractor.extract(fromOAuth2JS: minified)
    try expect(single?.clientID == "id-single", "single-quoted minified id must extract")
    try expect(single?.clientSecret == "secret-single", "single-quoted minified secret must extract")

    try expect(GeminiClientExtractor.extract(fromOAuth2JS: "const OAUTH_CLIENT_ID = \"only-id\";") == nil,
               "id without secret must fail extraction")
    try expect(GeminiClientExtractor.extract(fromOAuth2JS: "nothing here") == nil,
               "unrelated content must fail extraction")
}

private func testClientExtractionFromFixtureFile() throws {
    let home = try makeTempGeminiHome(credsExpiryMs: nil)
    defer { try? FileManager.default.removeItem(at: home) }
    let js = home.appendingPathComponent("oauth2.js")
    try """
    const OAUTH_CLIENT_ID = "file-client-id";
    const OAUTH_CLIENT_SECRET = "file-client-secret";
    """.write(to: js, atomically: true, encoding: .utf8)
    let content = try String(contentsOf: js, encoding: .utf8)
    let extracted = GeminiClientExtractor.extract(fromOAuth2JS: content)
    try expect(extracted == GeminiClientCredentials(
        clientID: "file-client-id", clientSecret: "file-client-secret"
    ), "extraction from a fixture js file must round-trip")
}

private func testClientEnvOverride() throws {
    let env = [
        "GEMINI_OAUTH_CLIENT_ID": "env-id",
        "GEMINI_OAUTH_CLIENT_SECRET": "env-secret",
    ]
    try expect(GeminiClientExtractor.fromEnvironment(env)
               == GeminiClientCredentials(clientID: "env-id", clientSecret: "env-secret"),
               "env override must resolve")
    try expect(GeminiClientExtractor.fromEnvironment(["GEMINI_OAUTH_CLIENT_ID": "only"]) == nil,
               "half an override must not resolve")
}

// MARK: - Quota decode

private let quotaFixture = Data("""
{
  "buckets": [
    { "modelId": "gemini-2.5-pro", "remainingFraction": 0.82,
      "resetTime": "2026-08-06T07:00:00Z" },
    { "modelId": "gemini-3-pro-preview", "remainingFraction": 0.35,
      "resetTime": "2026-08-06T07:00:00Z" },
    { "modelId": "gemini-3-flash-preview", "remainingFraction": 0.91,
      "resetTime": "2026-08-06T07:00:00Z" },
    { "modelId": "unknown-experimental" }
  ]
}
""".utf8)

private func testQuotaDecodesBucketsAndPicksLowestRemaining() throws {
    let buckets = AntigravityQuotaParser.parseQuota(quotaFixture)
    try expect(buckets?.count == 4, "every named bucket must decode")
    let snapshot = AntigravityQuotaSnapshot(buckets: buckets ?? [], tierID: nil, tierLabel: nil)
    try expect(snapshot.primaryPro?.modelId == "gemini-3-pro-preview",
               "the pro bucket with the lowest remaining must win the main bar")
    if let used = snapshot.primaryPro?.usedPercent {
        try expect(abs(used - 0.65) < 0.0001, "usedPercent must be 1 - remainingFraction")
    }
    try expect(snapshot.secondaryFlash?.modelId == "gemini-3-flash-preview",
               "the flash bucket must win the secondary")
    try expect(snapshot.primaryPro?.resetAt != nil, "resetTime must parse")
    let missingFraction = buckets?.first { $0.modelId == "unknown-experimental" }
    try expect(missingFraction?.usedPercent == 0,
               "a bucket without remainingFraction must read as untouched, not exhausted")
}

private func testQuotaEmptyAndGarbage() throws {
    let empty = AntigravityQuotaParser.parseQuota(Data("{}".utf8))
    try expect(empty != nil && empty?.isEmpty == true,
               "missing buckets must decode as an empty list, not a failure")
    let explicit = AntigravityQuotaParser.parseQuota(Data(#"{"buckets":[]}"#.utf8))
    try expect(explicit?.isEmpty == true, "an explicit empty buckets array must decode")
    try expect(AntigravityQuotaParser.parseQuota(Data("not json".utf8)) == nil,
               "non-JSON must be a parse failure")
    let snapshot = AntigravityQuotaSnapshot(buckets: [], tierID: nil, tierLabel: nil)
    try expect(snapshot.primaryPro == nil && snapshot.secondaryFlash == nil,
               "an empty snapshot must expose no bars")
}

private func testLoadCodeAssistParsing() throws {
    let paid = Data("""
    { "currentTier": { "id": "standard-tier" },
      "paidTier": { "id": "standard-tier", "name": "Google AI Pro" },
      "cloudaicompanionProject": "gen-lang-client-0123" }
    """.utf8)
    let profile = AntigravityQuotaParser.parseLoadCodeAssist(paid)
    try expect(profile?.tierID == "standard-tier", "currentTier.id must parse")
    try expect(profile?.tierLabel == "Google AI Pro", "paidTier.name must win the label")
    try expect(profile?.projectID == "gen-lang-client-0123", "companion project must parse")

    let free = Data(#"{ "currentTier": { "id": "free-tier" } }"#.utf8)
    let freeProfile = AntigravityQuotaParser.parseLoadCodeAssist(free)
    try expect(freeProfile?.tierLabel == "Free", "free-tier must map to Free")
    try expect(freeProfile?.projectID == nil, "missing project must read as nil")
}

private func testMigrationSignalDetection() throws {
    let unsupported = Data("""
    { "error": { "code": 403, "status": "PERMISSION_DENIED",
      "message": "UNSUPPORTED_CLIENT: this client is no longer supported" } }
    """.utf8)
    try expect(AntigravityQuotaParser.isMigrationSignal(unsupported),
               "UNSUPPORTED_CLIENT must read as the migration verdict")
    let ineligible = Data(#"{ "error": { "message": "IneligibleTierError" } }"#.utf8)
    try expect(AntigravityQuotaParser.isMigrationSignal(ineligible),
               "IneligibleTierError must read as the migration verdict")
    let antigravity = Data(#"{ "error": { "message": "Please migrate to Antigravity." } }"#.utf8)
    try expect(AntigravityQuotaParser.isMigrationSignal(antigravity),
               "Antigravity migration copy must read as the verdict")
    try expect(!AntigravityQuotaParser.isMigrationSignal(quotaFixture),
               "a healthy quota payload must not trip the migration signal")
}

@main
private enum GeminiParsingTestRunner {
    static func main() {
        let tests: [(String, () throws -> Void)] = [
            ("detection states", testDetectionStates),
            ("data root names and order", testDataRootNamesAndOrder),
            ("request text unwraps the USER_REQUEST envelope", testAntigravityRequestTextUnwrapsEnvelope),
            ("creds parse fields and email", testLoadCredsParsesFieldsAndEmail),
            ("needsRefresh honors skew", testNeedsRefreshHonorsSkew),
            ("refresh writeback rewrites atomically", testApplyRefreshRewritesAtomicallyAndPreservesEverythingElse),
            ("failed refresh leaves file untouched", testFailedRefreshLeavesFileUntouched),
            ("client extraction regex", testClientExtractionRegex),
            ("client extraction from fixture file", testClientExtractionFromFixtureFile),
            ("client env override", testClientEnvOverride),
            ("quota decodes buckets, lowest remaining wins", testQuotaDecodesBucketsAndPicksLowestRemaining),
            ("quota empty and garbage", testQuotaEmptyAndGarbage),
            ("loadCodeAssist parsing", testLoadCodeAssistParsing),
            ("migration signal detection", testMigrationSignalDetection)
        ]

        do {
            for (name, test) in tests {
                try test()
                print("PASS \(name)")
            }
            print("AntigravityParsingTests GREEN")
        } catch {
            fputs("AntigravityParsingTests RED: \(error)\n", stderr)
            exit(1)
        }
    }
}

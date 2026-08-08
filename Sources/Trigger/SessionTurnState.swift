import Foundation

struct SessionTurnStatus: Equatable {
    let isDone: Bool
    let key: String?
    let activityDate: Date?
}

enum SessionTurnState {
    static func claude(_ lines: [String]) -> SessionTurnStatus {
        for line in lines.reversed() {
            guard let object = json(line), let type = object["type"] as? String else { continue }
            // Older Claude Code interleaves subagent traffic into the main
            // transcript marked isSidechain — a subagent's end_turn there is
            // not the user's turn and must not classify the main session.
            if object["isSidechain"] as? Bool == true { continue }
            switch type {
            case "assistant":
                let stop = (object["message"] as? [String: Any])?["stop_reason"] as? String
                // Claude Code writes rate-limit / API-error lines using the SAME
                // envelope as a finished turn — type:assistant, stop_reason:
                // "stop_sequence" — but flags them isApiErrorMessage:true (e.g.
                // "You've hit your session limit · resets 2:20am"). Treating those
                // as a completed turn fired a false "it's your turn" alarm on
                // every rate-limit. A genuinely exhausted window now raises the
                // separate quota alarm instead; here we must NOT mark it done.
                let isApiError = (object["isApiErrorMessage"] as? Bool) == true
                return SessionTurnStatus(
                    isDone: !isApiError && ["end_turn", "stop_sequence", "stop"].contains(stop ?? ""),
                    key: key(object, fallback: line),
                    activityDate: date(object)
                )
            case "user":
                return SessionTurnStatus(isDone: false, key: key(object, fallback: line), activityDate: date(object))
            default:
                continue
            }
        }
        return SessionTurnStatus(isDone: false, key: nil, activityDate: nil)
    }

    static func codex(_ lines: [String]) -> SessionTurnStatus {
        for line in lines.reversed() {
            guard let object = json(line) else { continue }
            let type = object["type"] as? String
            let payload = object["payload"] as? [String: Any]
            let payloadType = payload?["type"] as? String
            if type == "event_msg" {
                if isCodexUserOrStart(payloadType) {
                    return SessionTurnStatus(isDone: false, key: key(object, fallback: line), activityDate: date(object))
                }
                if payloadType == "task_complete" || payloadType == "turn/completed" {
                    return SessionTurnStatus(isDone: true, key: key(object, fallback: line), activityDate: date(object))
                }
            }
            if type == "response_item" {
                if payloadType == "message", payload?["role"] as? String == "user" {
                    return SessionTurnStatus(isDone: false, key: key(object, fallback: line), activityDate: date(object))
                }
            }
        }
        return SessionTurnStatus(isDone: false, key: nil, activityDate: nil)
    }

    /// Grok appends one JSON object per session event to `updates.jsonl`
    /// with an explicit `sessionUpdate` discriminator — and unlike Claude,
    /// it names the turn boundary outright: `turn_completed`. Anything else
    /// after it (tool calls, streaming, retry_state) means the turn is
    /// still open. Timestamps are unix seconds.
    static func grok(_ lines: [String]) -> SessionTurnStatus {
        for line in lines.reversed() {
            guard let object = json(line),
                  let params = object["params"] as? [String: Any],
                  let update = params["update"] as? [String: Any],
                  let kind = update["sessionUpdate"] as? String
            else { continue }
            let stamp = (object["timestamp"] as? TimeInterval).map { Date(timeIntervalSince1970: $0) }
            return SessionTurnStatus(
                isDone: kind == "turn_completed",
                key: (stamp.map { String(Int($0.timeIntervalSince1970)) } ?? "") + ":" + kind,
                activityDate: stamp
            )
        }
        return SessionTurnStatus(isDone: false, key: nil, activityDate: nil)
    }

    /// Cursor: one bubble per message, `type` 1 = user, 2 = assistant
    /// (verified against live conversation text, 2026-08-08). The assistant
    /// having spoken last is the same turn boundary Claude's stop_reason
    /// gives us — the caller adds the quiet gap that separates "still
    /// streaming" from "finished".
    static func cursor(_ lines: [String]) -> SessionTurnStatus {
        guard let line = lines.last,
              let object = json(line) else {
            return SessionTurnStatus(isDone: false, key: nil, activityDate: nil)
        }
        let type = (object["type"] as? Int) ?? (object["type"] as? Double).map(Int.init) ?? 0
        let bubbleID = object["bubbleId"] as? String
        return SessionTurnStatus(
            isDone: type == 2,
            key: bubbleID.map { "cursor:" + $0 },
            activityDate: cursorDate(object["createdAt"])
        )
    }

    private static func cursorDate(_ raw: Any?) -> Date? {
        if let seconds = raw as? Double {
            return Date(timeIntervalSince1970: seconds > 100_000_000_000 ? seconds / 1000 : seconds)
        }
        if let text = raw as? String { return parseISO8601(text) }
        return nil
    }

    /// For sessions whose transcript has no explicit turn boundary yet
    /// (Gemini's $set checkpoint stream): never claims "done", so the
    /// engine derives working/idle purely from file recency and can never
    /// raise a false "your turn" alarm on a format we have not verified.
    static func mtimeOnly(_ lines: [String]) -> SessionTurnStatus {
        SessionTurnStatus(isDone: false, key: nil, activityDate: nil)
    }

    private static func isCodexUserOrStart(_ type: String?) -> Bool {
        guard let type else { return false }
        return type == "task_started"
            || type == "turn/started"
            || type == "user_message"
            || type.hasSuffix("/task_started")
            || type.hasSuffix("/user_message")
    }

    private static func key(_ object: [String: Any], fallback line: String) -> String {
        if let uuid = object["uuid"] as? String, !uuid.isEmpty { return uuid }
        if let id = object["id"] as? String, !id.isEmpty { return id }
        if let payload = object["payload"] as? [String: Any] {
            for field in ["turn_id", "id", "item_id", "call_id"] {
                if let value = payload[field] as? String, !value.isEmpty { return value }
            }
        }
        guard let data = line.data(using: .utf8) else { return String(line.prefix(120)) }
        return String(data.base64EncodedString().prefix(160))
    }

    private static func date(_ object: [String: Any]) -> Date? {
        if let timestamp = object["timestamp"] as? String, let parsed = parseISO8601(timestamp) {
            return parsed
        }
        if let payload = object["payload"] as? [String: Any] {
            for field in ["completed_at", "started_at"] {
                if let seconds = payload[field] as? TimeInterval {
                    return Date(timeIntervalSince1970: seconds)
                }
            }
        }
        return nil
    }

    private static func parseISO8601(_ raw: String) -> Date? {
        if let date = fractionalISO8601.date(from: raw) { return date }
        return plainISO8601.date(from: raw)
    }

    private static let fractionalISO8601: ISO8601DateFormatter = {
        let formatter = ISO8601DateFormatter()
        formatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return formatter
    }()

    private static let plainISO8601: ISO8601DateFormatter = {
        let formatter = ISO8601DateFormatter()
        formatter.formatOptions = [.withInternetDateTime]
        return formatter
    }()

    private static func json(_ line: String) -> [String: Any]? {
        guard let data = line.data(using: .utf8) else { return nil }
        return (try? JSONSerialization.jsonObject(with: data)) as? [String: Any]
    }
}

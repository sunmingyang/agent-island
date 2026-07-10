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

private func testClaudeUserAfterAssistantIsNotNeedsYou() throws {
    let expected = try date("2026-07-02T01:15:42.000Z")
    let lines = [
        #"{"type":"assistant","uuid":"a1","timestamp":"2026-06-25T16:27:50.585Z","message":{"stop_reason":"end_turn"}}"#,
        #"{"type":"custom-title","sessionId":"s1"}"#,
        #"{"type":"user","uuid":"u2","timestamp":"2026-07-02T01:15:42.000Z","message":{"role":"user","content":"next turn"}}"#
    ]
    let state = SessionTurnState.claude(lines)
    try expect(state.isDone == false, "new user turn after assistant must suppress stale needs-you")
    try expect(state.key == "u2", "latest user event should become the turn key")
    try expect(state.activityDate == expected, "latest user event should carry its timestamp")
}

private func testClaudeMetadataAfterAssistantKeepsAssistantTurnKey() throws {
    let expected = try date("2026-06-25T16:27:50.585Z")
    let lines = [
        #"{"type":"assistant","uuid":"a1","timestamp":"2026-06-25T16:27:50.585Z","message":{"stop_reason":"end_turn"}}"#,
        #"{"type":"mode","sessionId":"s1"}"#,
        #"{"type":"custom-title","sessionId":"s1"}"#
    ]
    let state = SessionTurnState.claude(lines)
    try expect(state.isDone == true, "metadata after assistant should not hide a completed turn")
    try expect(state.key == "a1", "metadata writes should not create a new delivery key")
    try expect(state.activityDate == expected, "metadata writes should not change semantic activity time")
}

private func testSidechainEndTurnDoesNotFinishMainTurn() throws {
    // Older Claude Code interleaves subagent lines into the main transcript.
    // A subagent finishing mid-fan-out must not read as "the user is up"
    // while the main turn is still running.
    let lines = [
        #"{"type":"user","uuid":"u1","timestamp":"2026-07-09T10:00:00.000Z","message":{"role":"user","content":"go"}}"#,
        #"{"type":"assistant","uuid":"a1","timestamp":"2026-07-09T10:00:05.000Z","message":{"stop_reason":"tool_use"}}"#,
        #"{"type":"assistant","uuid":"side1","isSidechain":true,"timestamp":"2026-07-09T10:00:30.000Z","message":{"stop_reason":"end_turn"}}"#
    ]
    let state = SessionTurnState.claude(lines)
    try expect(state.isDone == false, "a sidechain end_turn must not finish the main turn")
    try expect(state.key == "a1", "turn key must come from the last main line, not the sidechain")
}

private func testMainEndTurnSurvivesTrailingSidechain() throws {
    let lines = [
        #"{"type":"assistant","uuid":"a9","timestamp":"2026-07-09T10:05:00.000Z","message":{"stop_reason":"end_turn"}}"#,
        #"{"type":"user","uuid":"side2","isSidechain":true,"timestamp":"2026-07-09T10:05:02.000Z","message":{"role":"user","content":"tool result"}}"#
    ]
    let state = SessionTurnState.claude(lines)
    try expect(state.isDone == true, "trailing sidechain lines must not hide a finished main turn")
    try expect(state.key == "a9", "the main assistant line keeps the turn key")
}

private func testCodexUserAfterTaskCompleteIsNotNeedsYou() throws {
    let expected = try date("2026-07-02T01:15:42.000Z")
    let lines = [
        #"{"timestamp":"2026-07-02T01:14:00.000Z","type":"event_msg","payload":{"type":"task_complete","turn_id":"t1"}}"#,
        #"{"timestamp":"2026-07-02T01:15:42.000Z","type":"event_msg","payload":{"type":"user_message","turn_id":"t2","message":"next"}}"#
    ]
    let state = SessionTurnState.codex(lines)
    try expect(state.isDone == false, "new codex user message after task complete must suppress stale needs-you")
    try expect(state.key == "t2", "latest codex user event should become the turn key")
    try expect(state.activityDate == expected, "latest codex user event should carry its timestamp")
}

private func testCodexTaskStartedAfterTaskCompleteIsNotNeedsYou() throws {
    let lines = [
        #"{"type":"event_msg","payload":{"type":"task_complete","turn_id":"t1"}}"#,
        #"{"type":"event_msg","payload":{"type":"task_started","turn_id":"t2"}}"#
    ]
    let state = SessionTurnState.codex(lines)
    try expect(state.isDone == false, "new codex task start after completion must suppress stale needs-you")
    try expect(state.key == "t2", "latest codex task start should become the turn key")
}

private func testCodexTaskCompleteIsNeedsYou() throws {
    let lines = [
        #"{"timestamp":"2026-07-02T01:14:00.000Z","type":"event_msg","payload":{"type":"task_started","turn_id":"t1"}}"#,
        #"{"timestamp":"2026-07-02T01:15:42.000Z","type":"event_msg","payload":{"type":"task_complete","turn_id":"t1"}}"#
    ]
    let state = SessionTurnState.codex(lines)
    try expect(state.isDone == true, "codex task complete should be needs-you")
    try expect(state.key == "t1", "completed codex task should keep its turn id")
}

private func testMetadataTouchedOldClaudeTranscriptIsIdle() throws {
    let tmp = FileManager.default.temporaryDirectory
        .appendingPathComponent("agent-island-session-state-\(UUID().uuidString).jsonl")
    defer { try? FileManager.default.removeItem(at: tmp) }
    let old = try date("2026-06-25T16:27:50.585Z")
    let now = try date("2026-07-02T01:15:42.000Z")
    let lines = [
        #"{"type":"assistant","uuid":"a1","timestamp":"2026-06-25T16:27:50.585Z","message":{"stop_reason":"end_turn"}}"#,
        #"{"type":"custom-title","sessionId":"s1"}"#,
        #"{"type":"mode","sessionId":"s1"}"#
    ].joined(separator: "\n")
    try lines.write(to: tmp, atomically: true, encoding: .utf8)
    try FileManager.default.setAttributes([.modificationDate: now], ofItemAtPath: tmp.path)
    let state = SessionScanner.sessionState(
        for: tmp.path,
        now: now,
        lastWorking: [:],
        turnState: SessionTurnState.claude
    )
    try expect(state.status == .idle, "metadata-only file writes must not resurrect old completed Claude turns")
    try expect(state.modified == old, "semantic modified time should come from the last real turn")
}

private func testFreshClaudeEndTurnIsImmediatelyNeedsYou() throws {
    let now = try date("2026-07-02T01:15:42.000Z")
    let tmp = try writeTranscript([
        #"{"type":"user","uuid":"u1","timestamp":"2026-07-02T01:15:00.000Z","message":{"role":"user","content":"run"}}"#,
        #"{"type":"assistant","uuid":"a1","timestamp":"2026-07-02T01:15:41.000Z","message":{"stop_reason":"end_turn"}}"#
    ], modified: now)
    defer { try? FileManager.default.removeItem(at: tmp) }
    let state = SessionScanner.sessionState(
        for: tmp.path,
        now: now,
        lastWorking: [:],
        turnState: SessionTurnState.claude
    )
    try expect(state.status == .needsYou, "fresh Claude end_turn should immediately become needs-you")
}

private func testClaudeDesktopNewerActivitySuppressesOldEndTurn() throws {
    let old = try date("2026-06-25T16:27:50.585Z")
    let desktopActivity = try date("2026-07-02T01:15:42.000Z")
    let now = try date("2026-07-02T01:15:44.000Z")
    let tmp = try writeTranscript([
        #"{"type":"assistant","uuid":"a1","timestamp":"2026-06-25T16:27:50.585Z","message":{"stop_reason":"end_turn"}}"#,
        #"{"type":"mode","sessionId":"s1"}"#
    ], modified: now)
    defer { try? FileManager.default.removeItem(at: tmp) }
    let state = SessionScanner.sessionState(
        for: tmp.path,
        now: now,
        lastWorking: [:],
        externalActivityDate: desktopActivity,
        turnState: SessionTurnState.claude
    )
    try expect(state.status == .working, "newer Claude Desktop activity should suppress stale end_turn and show running")
    try expect(state.modified == desktopActivity, "newer desktop activity should drive the effective state time")
    try expect(old < state.modified, "test fixture must keep transcript older than desktop activity")
}

private func testDesktopBookkeepingWriteDoesNotSuppressFreshEndTurn() throws {
    // Desktop stamps lastActivityAt ~2-4s after the final assistant event as
    // turn-completion bookkeeping. That write must not eat the alarm.
    let turnDone = try date("2026-07-02T01:15:41.000Z")
    let bookkeeping = turnDone.addingTimeInterval(3)
    let now = try date("2026-07-02T01:15:48.000Z")
    let tmp = try writeTranscript([
        #"{"type":"user","uuid":"u1","timestamp":"2026-07-02T01:15:00.000Z","message":{"role":"user","content":"run"}}"#,
        #"{"type":"assistant","uuid":"a1","timestamp":"2026-07-02T01:15:41.000Z","message":{"stop_reason":"end_turn"}}"#
    ], modified: now)
    defer { try? FileManager.default.removeItem(at: tmp) }
    let state = SessionScanner.sessionState(
        for: tmp.path,
        now: now,
        lastWorking: [:],
        externalActivityDate: bookkeeping,
        turnState: SessionTurnState.claude
    )
    try expect(state.status == .needsYou, "desktop bookkeeping write seconds after end_turn must not suppress needs-you")
}

private func testDesktopActivityWellAfterEndTurnStillSuppresses() throws {
    // Past the bookkeeping grace the user genuinely came back — suppress.
    let turnDone = try date("2026-07-02T01:15:41.000Z")
    let userReturned = turnDone.addingTimeInterval(120)
    let now = try date("2026-07-02T01:17:45.000Z")
    let tmp = try writeTranscript([
        #"{"type":"user","uuid":"u1","timestamp":"2026-07-02T01:15:00.000Z","message":{"role":"user","content":"run"}}"#,
        #"{"type":"assistant","uuid":"a1","timestamp":"2026-07-02T01:15:41.000Z","message":{"stop_reason":"end_turn"}}"#
    ], modified: now)
    defer { try? FileManager.default.removeItem(at: tmp) }
    let state = SessionScanner.sessionState(
        for: tmp.path,
        now: now,
        lastWorking: [:],
        externalActivityDate: userReturned,
        turnState: SessionTurnState.claude
    )
    try expect(state.status == .working, "desktop activity well after end_turn means the user is back — suppress the alarm")
}

private func testClaudeStreamingAssistantIsWorking() throws {
    let now = try date("2026-07-02T01:15:44.000Z")
    let tmp = try writeTranscript([
        #"{"type":"user","uuid":"u1","timestamp":"2026-07-02T01:15:00.000Z","message":{"role":"user","content":"run"}}"#,
        #"{"type":"assistant","uuid":"a1","timestamp":"2026-07-02T01:15:42.000Z","message":{"stop_reason":null}}"#
    ], modified: now)
    defer { try? FileManager.default.removeItem(at: tmp) }
    let state = SessionScanner.sessionState(
        for: tmp.path,
        now: now,
        lastWorking: [:],
        turnState: SessionTurnState.claude
    )
    try expect(state.status == .working, "Claude assistant output without end_turn should stay running")
}

private func writeTranscript(_ lines: [String], modified: Date) throws -> URL {
    let tmp = FileManager.default.temporaryDirectory
        .appendingPathComponent("agent-island-session-state-\(UUID().uuidString).jsonl")
    try lines.joined(separator: "\n").write(to: tmp, atomically: true, encoding: .utf8)
    try FileManager.default.setAttributes([.modificationDate: modified], ofItemAtPath: tmp.path)
    return tmp
}

private func date(_ raw: String) throws -> Date {
    let formatter = ISO8601DateFormatter()
    formatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
    guard let date = formatter.date(from: raw) else {
        throw TestFailure.assertion("bad test date \(raw)")
    }
    return date
}

private func testClaudeApiErrorLineIsNotNeedsYou() throws {
    // A rate-limit / API-error line uses the same envelope as a finished turn
    // (type:assistant, stop_reason:"stop_sequence") but carries
    // isApiErrorMessage:true. It must NOT be treated as your-turn.
    let lines = [
        #"{"type":"assistant","uuid":"a1","timestamp":"2026-07-02T01:15:42.000Z","isApiErrorMessage":true,"message":{"stop_reason":"stop_sequence","content":"You've hit your session limit · resets 2:20am"}}"#
    ]
    let state = SessionTurnState.claude(lines)
    try expect(state.isDone == false, "an API-error / rate-limit assistant line must not count as a finished turn")
}

@main
private enum SessionTurnStateTestRunner {
    static func main() {
        let tests: [(String, () throws -> Void)] = [
            ("claude user after assistant suppresses stale alarm", testClaudeUserAfterAssistantIsNotNeedsYou),
            ("claude metadata does not change turn key", testClaudeMetadataAfterAssistantKeepsAssistantTurnKey),
            ("sidechain end_turn does not finish the main turn", testSidechainEndTurnDoesNotFinishMainTurn),
            ("main end_turn survives trailing sidechain lines", testMainEndTurnSurvivesTrailingSidechain),
            ("codex user after complete suppresses stale alarm", testCodexUserAfterTaskCompleteIsNotNeedsYou),
            ("codex start after complete suppresses stale alarm", testCodexTaskStartedAfterTaskCompleteIsNotNeedsYou),
            ("codex complete still triggers needs-you", testCodexTaskCompleteIsNeedsYou),
            ("metadata-touched old claude transcript is idle", testMetadataTouchedOldClaudeTranscriptIsIdle),
            ("fresh claude end_turn is immediately needs-you", testFreshClaudeEndTurnIsImmediatelyNeedsYou),
            ("newer claude desktop activity suppresses old end_turn", testClaudeDesktopNewerActivitySuppressesOldEndTurn),
            ("desktop bookkeeping write does not suppress fresh end_turn", testDesktopBookkeepingWriteDoesNotSuppressFreshEndTurn),
            ("desktop activity well after end_turn still suppresses", testDesktopActivityWellAfterEndTurnStillSuppresses),
            ("claude streaming assistant is working", testClaudeStreamingAssistantIsWorking),
            ("claude api-error / rate-limit line is not needs-you", testClaudeApiErrorLineIsNotNeedsYou)
        ]

        do {
            for (name, test) in tests {
                try test()
                print("PASS \(name)")
            }
            print("SessionTurnStateTests GREEN")
        } catch {
            fputs("SessionTurnStateTests RED: \(error)\n", stderr)
            exit(1)
        }
    }
}

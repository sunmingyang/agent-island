import Foundation
import CoreServices

/// FSEvents watcher over the transcript roots. Polling alone means a state
/// change waits up to a full tick to surface; file events let the monitor
/// react the moment a transcript line lands, so the logo starts and stops
/// with the run instead of seconds behind it. The poll stays as a fallback
/// sweep for anything events miss.
final class TranscriptEventStream {
    private var stream: FSEventStreamRef?
    private let queue = DispatchQueue(label: "TranscriptEventStream", qos: .utility)
    private let onChange: () -> Void

    init(onChange: @escaping () -> Void) {
        self.onChange = onChange
    }

    deinit {
        if let stream {
            FSEventStreamStop(stream)
            FSEventStreamInvalidate(stream)
            FSEventStreamRelease(stream)
        }
    }

    func start() {
        guard stream == nil else { return }
        let home = NSHomeDirectory()
        let roots = [
            home + "/.claude/projects",
            home + "/.codex/sessions",
            home + "/Library/Application Support/Claude/claude-code-sessions",
        ].filter { FileManager.default.fileExists(atPath: $0) }
        guard !roots.isEmpty else { return }
        var context = FSEventStreamContext(
            version: 0,
            info: Unmanaged.passUnretained(self).toOpaque(),
            retain: nil,
            release: nil,
            copyDescription: nil
        )
        let callback: FSEventStreamCallback = { _, info, count, paths, _, _ in
            guard let info, count > 0 else { return }
            let watcher = Unmanaged<TranscriptEventStream>.fromOpaque(info).takeUnretainedValue()
            guard let changed = unsafeBitCast(paths, to: NSArray.self) as? [String] else { return }
            if changed.contains(where: { watcher.isRelevant($0) }) {
                watcher.onChange()
            }
        }
        guard let created = FSEventStreamCreate(
            nil,
            callback,
            &context,
            roots as CFArray,
            FSEventStreamEventId(kFSEventStreamEventIdSinceNow),
            0.2,
            FSEventStreamCreateFlags(kFSEventStreamCreateFlagUseCFTypes | kFSEventStreamCreateFlagFileEvents | kFSEventStreamCreateFlagNoDefer)
        ) else { return }
        stream = created
        FSEventStreamSetDispatchQueue(created, queue)
        FSEventStreamStart(created)
    }

    private func isRelevant(_ path: String) -> Bool {
        path.hasSuffix(".jsonl") || (path as NSString).lastPathComponent.hasPrefix("local_")
    }
}

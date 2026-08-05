import Foundation

/// Parses `~/.ssh/config` for `Host` aliases so the remote-server settings
/// form can offer a dropdown of known machines. Pure text parsing — no
/// network, no keychain, safe on any thread.
enum SSHConfigParser {
    /// The aliases declared via `Host` in the SSH config, in file order,
    /// deduped. Wildcards (`*`, `?`) and empty tokens are dropped — a
    /// `Host *.example.com` match-all entry is a pattern, not a server the
    /// user can sync from by name.
    static func hostAliases(configPath: String? = nil) -> [String] {
        let path = configPath ?? "\(NSHomeDirectory())/.ssh/config"

        // mtime-keyed cache: the settings form re-renders often, and parsing
        // the file on every pass is wasted work for an input that changes
        // rarely. Guarded so the cost-scanner threads could use this too.
        let mtime = (try? FileManager.default.attributesOfItem(atPath: path)[.modificationDate]) as? Date
        lock.lock()
        if path == cachePath, mtime == cacheMtime {
            let cached = cacheResult
            lock.unlock()
            return cached
        }
        lock.unlock()

        guard let text = try? String(contentsOfFile: path, encoding: .utf8) else { return [] }

        var aliases: [String] = []
        for rawLine in text.split(separator: "\n") {
            // Strip inline comments before anything else. A line that is
            // exactly "#" splits to an EMPTY array (the whole line is the
            // separator) — map to "" instead of force-unwrapping.
            let beforeComment = rawLine.split(separator: "#", maxSplits: 1).first.map(String.init) ?? ""
            let line = beforeComment.trimmingCharacters(in: .whitespacesAndNewlines)
            guard !line.isEmpty else { continue }

            // The line's FIRST token must be exactly `host` (case-insensitive)
            // — `HostName`, `HostKeyAlias` etc. must not match.
            let tokens = line.split(whereSeparator: { $0 == " " || $0 == "\t" })
            guard let first = tokens.first, first.lowercased() == "host", tokens.count > 1 else { continue }

            // Aliases are the remaining tokens; they can be space- or
            // comma-separated (`Host vm1 vm2` / `Host prod,staging`).
            for token in tokens.dropFirst() {
                for piece in token.split(separator: ",") {
                    let alias = piece.trimmingCharacters(in: .whitespaces)
                    if alias.isEmpty { continue }
                    if alias.contains("*") || alias.contains("?") { continue }
                    if !aliases.contains(alias) { aliases.append(alias) }
                }
            }
        }

        lock.lock()
        cachePath = path
        cacheMtime = mtime
        cacheResult = aliases
        lock.unlock()
        return aliases
    }

    private static let lock = NSLock()
    private static var cachePath: String?
    private static var cacheMtime: Date?
    private static var cacheResult: [String] = []
}

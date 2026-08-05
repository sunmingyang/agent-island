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
        guard let text = try? String(contentsOfFile: path, encoding: .utf8) else { return [] }

        var aliases: [String] = []
        for rawLine in text.split(separator: "\n") {
            // Strip inline comments before anything else.
            let line = rawLine.split(separator: "#", maxSplits: 1).first!
                .trimmingCharacters(in: .whitespacesAndNewlines)
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
        return aliases
    }
}

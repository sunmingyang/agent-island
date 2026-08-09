import Darwin
import Foundation

/// Client for Antigravity's local language server — the only place its quota
/// is actually readable.
///
/// The cloud path every other tool documents is a dead end here, verified
/// against a real signed-in account (2026-08-08): `cloudcode-pa` /
/// `daily-cloudcode-pa` `retrieveUserQuota` answers 200 but with legacy
/// Gemini Code Assist buckets (all at 1.0, models the CLI does not even
/// run), `loadCodeAssist` answers `UNSUPPORTED_CLIENT` telling the caller to
/// migrate to Antigravity, and `retrieveUserQuotaSummary` answers 403 for a
/// plain Bearer caller. The CLI reaches the same method through its own
/// language server, which does hold the real numbers.
///
/// That server is embedded in the `agy` process rather than spawned
/// separately, so quota is readable exactly while Antigravity is running.
/// There is no on-disk cache to fall back on (`cache/` holds only
/// onboarding and conversation metadata), so the store keeps the last good
/// snapshot and the UI dates it.
enum AntigravityLanguageServer {
    private static let servicePrefix = "/exa.language_server_pb.LanguageServerService/"
    private static let host = "127.0.0.1"

    struct Reply {
        let status: Int
        let body: Data
    }

    // MARK: - Transport

    /// The server speaks HTTPS with a self-signed certificate. The trust
    /// override is scoped to 127.0.0.1 so this can never soften validation
    /// for a real host.
    private final class LocalhostTrust: NSObject, URLSessionDelegate {
        func urlSession(
            _ session: URLSession,
            didReceive challenge: URLAuthenticationChallenge,
            completionHandler: @escaping (URLSession.AuthChallengeDisposition, URLCredential?) -> Void
        ) {
            guard challenge.protectionSpace.authenticationMethod == NSURLAuthenticationMethodServerTrust,
                  challenge.protectionSpace.host == AntigravityLanguageServer.host,
                  let trust = challenge.protectionSpace.serverTrust else {
                completionHandler(.performDefaultHandling, nil)
                return
            }
            completionHandler(.useCredential, URLCredential(trust: trust))
        }
    }

    private static let session: URLSession = {
        let config = URLSessionConfiguration.ephemeral
        config.timeoutIntervalForRequest = 6
        config.waitsForConnectivity = false
        // Never let a system proxy swallow a loopback call.
        config.connectionProxyDictionary = [:]
        return URLSession(configuration: config, delegate: LocalhostTrust(), delegateQueue: nil)
    }()

    static func call(
        _ method: String,
        port: UInt16,
        body: Data = Data("{}".utf8),
        csrfToken: String? = nil,
        timeout: TimeInterval = 6
    ) async -> Reply? {
        guard let url = URL(string: "https://\(host):\(port)\(servicePrefix)\(method)") else { return nil }
        var request = URLRequest(url: url, timeoutInterval: timeout)
        request.httpMethod = "POST"
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.setValue("1", forHTTPHeaderField: "Connect-Protocol-Version")
        // The CLI's own server rejects nothing and wants no token; the
        // desktop IDE gates on one. Sending an empty header to the CLI would
        // be worse than sending none, so it stays absent unless we have one.
        if let csrfToken, !csrfToken.isEmpty {
            request.setValue(csrfToken, forHTTPHeaderField: "X-Codeium-Csrf-Token")
        }
        request.httpBody = body
        guard let (data, response) = try? await session.data(for: request),
              let http = response as? HTTPURLResponse else { return nil }
        return Reply(status: http.statusCode, body: data)
    }

    // MARK: - Discovery

    /// Ports are assigned at launch and written nowhere, so they are found by
    /// walking the running Antigravity processes' listening sockets. libproc
    /// does this in-process; shelling out to `lsof` on every refresh tick
    /// would fork twice a minute for the life of the app.
    static func discover() async -> UInt16? {
        if let cached = cachedPort, await isAlive(cached) { return cached }
        for pid in antigravityProcesses() {
            for port in listeningPorts(pid) where port != cachedPort {
                if await isAlive(port) {
                    cachedPort = port
                    return port
                }
            }
        }
        cachedPort = nil
        return nil
    }

    private nonisolated(unsafe) static var cachedPort: UInt16?

    /// `GetUnleashData` is the cheapest method that proves this is the RPC
    /// port rather than the sibling plain-HTTP port the process also opens.
    /// 401 counts as alive: the port is right and only the token is missing.
    private static func isAlive(_ port: UInt16) async -> Bool {
        guard let reply = await call(
            "GetUnleashData",
            port: port,
            body: Data(#"{"wrapper_data":{}}"#.utf8),
            timeout: 2
        ) else { return false }
        return reply.status == 200 || reply.status == 401
    }

    /// Matches the CLI (whose real binary is `antigravity`, reached through
    /// an `agy` symlink), the desktop app, and a separately spawned
    /// `language_server*`. The bare-name check is exact rather than a
    /// substring so `/usr/bin/legacy` cannot match "agy".
    static func isAntigravityPath(_ path: String) -> Bool {
        guard !path.isEmpty else { return false }
        let name = (path as NSString).lastPathComponent
        if name == "agy" || name == "antigravity" || name == "antigravity-cli" { return true }
        if name.hasPrefix("language_server") { return true }
        return path.lowercased().contains("/antigravity")
    }

    static func antigravityProcesses() -> [pid_t] {
        var out: [pid_t] = []
        for pid in allProcesses() where isAntigravityPath(executablePath(pid)) {
            out.append(pid)
        }
        return out
    }

    private static func allProcesses() -> [pid_t] {
        let capacity = proc_listpids(UInt32(PROC_ALL_PIDS), 0, nil, 0)
        guard capacity > 0 else { return [] }
        var buffer = [pid_t](repeating: 0, count: Int(capacity) / MemoryLayout<pid_t>.size)
        let used = proc_listpids(UInt32(PROC_ALL_PIDS), 0, &buffer, capacity)
        guard used > 0 else { return [] }
        return buffer.prefix(Int(used) / MemoryLayout<pid_t>.size).filter { $0 > 0 }
    }

    private static func executablePath(_ pid: pid_t) -> String {
        // PROC_PIDPATHINFO_MAXSIZE (4 * MAXPATHLEN) is not surfaced to Swift.
        var buffer = [CChar](repeating: 0, count: 4096)
        guard proc_pidpath(pid, &buffer, UInt32(buffer.count)) > 0 else { return "" }
        return String(cString: buffer)
    }

    /// The process's current working directory, kernel-resolved (so /tmp
    /// reads as /private/tmp). Used to match a running agy to the
    /// conversation an alarm points at.
    static func currentWorkingDirectory(_ pid: pid_t) -> String? {
        var info = proc_vnodepathinfo()
        let size = Int32(MemoryLayout<proc_vnodepathinfo>.size)
        guard proc_pidinfo(pid, PROC_PIDVNODEPATHINFO, 0, &info, size) > 0 else { return nil }
        let path = withUnsafeBytes(of: info.pvi_cdir.vip_path) { raw -> String in
            guard let base = raw.bindMemory(to: CChar.self).baseAddress else { return "" }
            return String(cString: base)
        }
        return path.isEmpty ? nil : path
    }

    /// Walks up the process tree to the GUI application that owns this
    /// process's terminal session — Terminal, iTerm, VS Code, whatever the
    /// user runs agy inside. The `.app/Contents/MacOS/` marker separates an
    /// application from the shells in between (agy ← zsh ← login ←
    /// Terminal). A headless agy (launchd, CI) never reaches one and
    /// returns nil, which is exactly the "nothing to front" answer.
    ///
    /// The parent hop uses sysctl, not proc_pidinfo: Terminal's `login`
    /// intermediary runs as root, proc_pidinfo denies it to a user process,
    /// and the walk died right there on the owner's machine (2026-08-09).
    /// The kinfo_proc table is world-readable.
    static func owningGUIApplication(_ pid: pid_t) -> pid_t? {
        var current = pid
        for _ in 0..<12 {
            if executablePath(current).contains(".app/Contents/MacOS/") { return current }
            guard let parent = parentPID(current), parent > 1, parent != current else {
                return nil
            }
            current = parent
        }
        return nil
    }

    private static func parentPID(_ pid: pid_t) -> pid_t? {
        var info = kinfo_proc()
        var size = MemoryLayout<kinfo_proc>.stride
        var mib: [Int32] = [CTL_KERN, KERN_PROC, KERN_PROC_PID, pid]
        guard sysctl(&mib, 4, &info, &size, nil, 0) == 0, size > 0 else { return nil }
        return info.kp_eproc.e_ppid
    }

    static func listeningPorts(_ pid: pid_t) -> [UInt16] {
        let size = proc_pidinfo(pid, PROC_PIDLISTFDS, 0, nil, 0)
        guard size > 0 else { return [] }
        let stride = MemoryLayout<proc_fdinfo>.stride
        var descriptors = [proc_fdinfo](repeating: proc_fdinfo(), count: Int(size) / stride)
        let used = proc_pidinfo(pid, PROC_PIDLISTFDS, 0, &descriptors, size)
        guard used > 0 else { return [] }

        var ports: [UInt16] = []
        for descriptor in descriptors.prefix(Int(used) / stride)
        where descriptor.proc_fdtype == UInt32(PROX_FDTYPE_SOCKET) {
            var info = socket_fdinfo()
            let read = proc_pidfdinfo(
                pid, descriptor.proc_fd, PROC_PIDFDSOCKETINFO,
                &info, Int32(MemoryLayout<socket_fdinfo>.size)
            )
            guard read > 0, info.psi.soi_kind == SOCKINFO_TCP else { continue }
            let tcp = info.psi.soi_proto.pri_tcp
            guard tcp.tcpsi_state == Int32(TSI_S_LISTEN) else { continue }
            let port = UInt16(bigEndian: UInt16(truncatingIfNeeded: tcp.tcpsi_ini.insi_lport))
            if port > 0, !ports.contains(port) { ports.append(port) }
        }
        return ports
    }

    // MARK: - CSRF (desktop IDE only)

    /// The CLI needs no token and must not be sent one. The desktop IDE
    /// passes its own on the command line, so it is lifted from argv only
    /// after a 401 says the plain call was refused. Unverified against a real
    /// IDE install — it is a strictly additive retry on a path that already
    /// failed, never part of the working CLI call.
    static func csrfToken(pid: pid_t) -> String? {
        guard let argv = processArguments(pid) else { return nil }
        guard let range = argv.range(
            of: #"--csrf_token[=\s]+([^\s]+)"#,
            options: .regularExpression
        ) else { return nil }
        let match = String(argv[range])
        guard let valueStart = match.rangeOfCharacter(
            from: CharacterSet(charactersIn: "= "), options: .backwards
        ) else { return nil }
        let token = String(match[valueStart.upperBound...])
        return token.isEmpty ? nil : token
    }

    private static func processArguments(_ pid: pid_t) -> String? {
        var argmax: Int32 = 0
        var size = MemoryLayout<Int32>.size
        var maxName: [Int32] = [CTL_KERN, KERN_ARGMAX]
        guard sysctl(&maxName, 2, &argmax, &size, nil, 0) == 0, argmax > 0 else { return nil }

        var buffer = [CChar](repeating: 0, count: Int(argmax))
        var length = Int(argmax)
        var argsName: [Int32] = [CTL_KERN, KERN_PROCARGS2, pid]
        guard sysctl(&argsName, 3, &buffer, &length, nil, 0) == 0, length > 0 else { return nil }

        // The blob is NUL-separated; joining with spaces is enough for the
        // regex above and avoids decoding the argc/exec-path preamble.
        let bytes = buffer.prefix(length).map { UInt8(bitPattern: $0) }
        let parts = bytes.split(separator: 0).map { String(decoding: $0, as: UTF8.self) }
        return parts.joined(separator: " ")
    }
}

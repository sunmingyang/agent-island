import AppKit
import SwiftUI
import Network
import CoreImage

/// "发到手机" — the only honest path to WeChat Moments / Xiaohongshu / Douyin
/// on macOS. None of them ship a mac share SDK (the native share panel the
/// user knows from iOS is WeChat's *iOS* OpenSDK), so posting natively means
/// putting the image on the phone: the Mac serves the PNG over the local
/// network for a few minutes, the phone scans a QR, and WeChat's own
/// long-press menu takes it from there. Nothing ever leaves the LAN — no
/// accounts, no servers, on brand.
@MainActor
final class HandoffServer {
    static let shared = HandoffServer()

    private var listener: NWListener?
    private var png = Data()
    private var token = ""
    private var stopTask: Task<Void, Never>?

    /// Starts (or restarts) the one-image server. Returns the URL the phone
    /// should open, or nil when there's no usable LAN address.
    func start(png: Data) -> URL? {
        stop()
        guard let ip = Self.lanIPv4() else { return nil }
        self.png = png
        token = String(UUID().uuidString.prefix(8)).lowercased()

        // Bind an ephemeral-range port ourselves so the URL is known
        // synchronously; retry a few times on collision.
        for _ in 0..<6 {
            let port = UInt16.random(in: 49200...65200)
            guard let nwPort = NWEndpoint.Port(rawValue: port) else { continue }
            let params = NWParameters.tcp
            params.allowLocalEndpointReuse = true
            guard let l = try? NWListener(using: params, on: nwPort) else { continue }
            l.newConnectionHandler = { [weak self] conn in
                // Connection handling is thread-safe: it only reads the
                // captured snapshot of png/token via the handler below.
                self?.handle(conn)
            }
            l.start(queue: .global(qos: .userInitiated))
            listener = l

            // Serving an image to your own phone shouldn't outlive the
            // moment — kill the listener after 15 minutes no matter what.
            stopTask = Task { [weak self] in
                try? await Task.sleep(nanoseconds: 15 * 60 * 1_000_000_000)
                self?.stop()
            }
            return URL(string: "http://\(ip):\(port)/r/\(token)")
        }
        return nil
    }

    func stop() {
        stopTask?.cancel()
        stopTask = nil
        listener?.cancel()
        listener = nil
        png = Data()
        token = ""
    }

    // MARK: - Request handling

    private nonisolated func handle(_ conn: NWConnection) {
        conn.start(queue: .global(qos: .userInitiated))
        conn.receive(minimumIncompleteLength: 1, maximumLength: 16384) { data, _, _, _ in
            guard let data, let head = String(data: data, encoding: .utf8) else {
                conn.cancel()
                return
            }
            let path = head.split(separator: " ").dropFirst().first.map(String.init) ?? "/"
            Task { @MainActor in
                let response = self.response(for: path)
                conn.send(content: response, completion: .contentProcessed { _ in
                    conn.cancel()
                })
            }
        }
    }

    private func response(for path: String) -> Data {
        guard !token.isEmpty else { return Self.http("404 Not Found", "text/plain", Data()) }
        switch path {
        case "/r/\(token)":
            return Self.http("200 OK", "text/html; charset=utf-8", Data(pageHTML().utf8))
        case "/img/\(token).png":
            return Self.http("200 OK", "image/png", png)
        default:
            return Self.http("404 Not Found", "text/plain", Data())
        }
    }

    private static func http(_ status: String, _ type: String, _ body: Data) -> Data {
        var head = "HTTP/1.1 \(status)\r\n"
        head += "Content-Type: \(type)\r\n"
        head += "Content-Length: \(body.count)\r\n"
        head += "Cache-Control: no-store\r\n"
        head += "Connection: close\r\n\r\n"
        return Data(head.utf8) + body
    }

    /// The page WeChat's built-in browser (or Safari) opens: the card
    /// full-width on dark, with the long-press hint. Bilingual, static.
    private func pageHTML() -> String {
        """
        <!doctype html><html><head><meta charset="utf-8">
        <meta name="viewport" content="width=device-width,initial-scale=1">
        <title>Agent Island</title>
        <style>
         body{margin:0;background:#0a0b0d;color:#fff;font-family:-apple-system,sans-serif;
              display:flex;flex-direction:column;align-items:center;padding:28px 18px 60px}
         img{width:100%;max-width:440px;border-radius:18px}
         p{font-size:14px;font-weight:600;color:rgba(255,255,255,.65);margin:18px 0 0;text-align:center}
         small{font-size:11px;color:rgba(255,255,255,.3);margin-top:8px}
        </style></head><body>
        <img src="/img/\(token).png" alt="Agent Island weekly report">
        <p>长按图片 → 共享 / 发送给朋友 / 保存<br>Long-press the image to share or save</p>
        <small>Agent Island · 局域网直传,不经过服务器</small>
        </body></html>
        """
    }

    // MARK: - LAN address

    /// First non-loopback IPv4 on an en* interface (Wi-Fi/Ethernet).
    private static func lanIPv4() -> String? {
        var addrs: UnsafeMutablePointer<ifaddrs>?
        guard getifaddrs(&addrs) == 0, let first = addrs else { return nil }
        defer { freeifaddrs(addrs) }

        var best: String?
        var cursor: UnsafeMutablePointer<ifaddrs>? = first
        while let ifa = cursor {
            defer { cursor = ifa.pointee.ifa_next }
            guard let sa = ifa.pointee.ifa_addr, sa.pointee.sa_family == UInt8(AF_INET) else { continue }
            let name = String(cString: ifa.pointee.ifa_name)
            guard name.hasPrefix("en") else { continue }
            var host = [CChar](repeating: 0, count: Int(NI_MAXHOST))
            guard getnameinfo(sa, socklen_t(sa.pointee.sa_len), &host, socklen_t(host.count),
                              nil, 0, NI_NUMERICHOST) == 0 else { continue }
            let ip = String(cString: host)
            guard !ip.hasPrefix("127.") else { continue }
            // en0 wins; anything else is a fallback.
            if name == "en0" { return ip }
            if best == nil { best = ip }
        }
        return best
    }
}

// MARK: - Window

/// Borderless QR panel, same shell as the report window (titled windows drag
/// the macOS 26 glass slab along).
private final class HandoffPanel: NSPanel {
    override var canBecomeKey: Bool { true }
    override func cancelOperation(_ sender: Any?) { close() }
}

@MainActor
final class PhoneHandoffWindowController: NSWindowController, NSWindowDelegate {
    static let shared = PhoneHandoffWindowController()

    private init() {
        super.init(window: nil)
    }

    @available(*, unavailable)
    required init?(coder: NSCoder) { fatalError() }

    /// One QR covers every platform: the phone's OWN share sheet (long-press
    /// in Safari/WeChat) reaches WeChat, WhatsApp, Instagram, X — anything
    /// installed. Renders from the warm cache, starts the LAN server, shows
    /// the QR.
    func show() {
        guard let png = WeeklyReportRenderer.pngData() else { return }
        let url = HandoffServer.shared.start(png: png)

        let size = NSSize(width: 400, height: 532)
        if window == nil {
            let panel = HandoffPanel(
                contentRect: NSRect(origin: .zero, size: size),
                styleMask: [.borderless, .fullSizeContentView],
                backing: .buffered,
                defer: false
            )
            panel.backgroundColor = .clear
            panel.isOpaque = false
            panel.hasShadow = false
            panel.isMovableByWindowBackground = true
            panel.hidesOnDeactivate = false
            panel.isReleasedWhenClosed = false
            panel.level = .floating
            panel.delegate = self
            window = panel
        }
        window?.contentView = NSHostingView(rootView: HandoffSheet(url: url))
        mountCloseButton()
        window?.center()
        NSApp.activate(ignoringOtherApps: true)
        window?.makeKeyAndOrderFront(nil)
    }

    func windowWillClose(_ notification: Notification) {
        HandoffServer.shared.stop()
    }

    private func mountCloseButton() {
        guard let contentView = window?.contentView,
              let close = NSWindow.standardWindowButton(.closeButton, for: [.titled, .closable])
        else { return }
        close.target = window
        close.action = #selector(NSWindow.close)
        contentView.addSubview(close)
        let inset: CGFloat = 12
        let yFromTop: CGFloat = 14 + inset
        let y = contentView.isFlipped
            ? yFromTop
            : contentView.bounds.height - yFromTop - close.frame.height
        close.setFrameOrigin(NSPoint(x: 14 + inset, y: y))
    }
}

private struct HandoffSheet: View {
    let url: URL?

    var body: some View {
        VStack(spacing: 16) {
            VStack(spacing: 4) {
                Text(L10n.tr("Send to your phone"))
                    .font(.system(size: 17, weight: .heavy, design: .rounded))
                    .foregroundStyle(.white)
                Text(L10n.tr("WeChat · WhatsApp · Instagram · X · anywhere"))
                    .font(.system(size: 11.5, weight: .bold, design: .rounded))
                    .foregroundStyle(.white.opacity(0.5))
            }
            .padding(.top, 34)

            if let url, let qr = Self.qrImage(for: url.absoluteString) {
                Image(nsImage: qr)
                    .interpolation(.none)
                    .resizable()
                    .frame(width: 216, height: 216)
                    .padding(10)
                    .background(RoundedRectangle(cornerRadius: 16).fill(.white))
            } else {
                Text(L10n.tr("Couldn't start local sharing — is Wi-Fi on?"))
                    .font(.system(size: 12.5, weight: .bold, design: .rounded))
                    .foregroundStyle(Color(red: 1.0, green: 0.55, blue: 0.4))
                    .frame(width: 236, height: 236)
            }

            Text(L10n.tr("Scan with your phone → long-press the image → share to any app, or save it"))
                .font(.system(size: 12.5, weight: .bold, design: .rounded))
                .foregroundStyle(.white.opacity(0.85))
                .multilineTextAlignment(.center)
                .lineSpacing(5)
                .padding(.horizontal, 26)

            // iPhone shortcut: AirDrop drops the PNG straight into Photos —
            // no scan, no Wi-Fi requirement beyond proximity.
            Button {
                if let image = WeeklyReportRenderer.image(),
                   let airdrop = NSSharingService(named: .sendViaAirDrop) {
                    airdrop.perform(withItems: [image])
                }
            } label: {
                Text(L10n.tr("Or AirDrop to your iPhone →"))
                    .font(.system(size: 11.5, weight: .bold, design: .rounded))
                    .foregroundStyle(.white.opacity(0.6))
            }
            .buttonStyle(.plain)

            Spacer(minLength: 0)

            Text(L10n.tr("Same Wi-Fi as your Mac · image travels over your local network only, no servers"))
                .font(.system(size: 10, weight: .semibold, design: .rounded))
                .foregroundStyle(.white.opacity(0.35))
                .multilineTextAlignment(.center)
                .padding(.horizontal, 30)
                .padding(.bottom, 18)
        }
        .frame(width: 400, height: 532)
        .background(
            RoundedRectangle(cornerRadius: 24)
                .fill(LinearGradient(colors: [Color(red: 0.09, green: 0.095, blue: 0.115),
                                              Color(red: 0.055, green: 0.06, blue: 0.075)],
                                     startPoint: .top, endPoint: .bottom))
                .overlay(RoundedRectangle(cornerRadius: 24)
                    .strokeBorder(.white.opacity(0.08), lineWidth: 1))
        )
        .shadow(color: .black.opacity(0.30), radius: 10, y: 4)
        .padding(6)
    }

    private static func qrImage(for string: String) -> NSImage? {
        let filter = CIFilter(name: "CIQRCodeGenerator")
        filter?.setValue(Data(string.utf8), forKey: "inputMessage")
        filter?.setValue("M", forKey: "inputCorrectionLevel")
        guard let output = filter?.outputImage else { return nil }
        let scaled = output.transformed(by: CGAffineTransform(scaleX: 12, y: 12))
        let rep = NSCIImageRep(ciImage: scaled)
        let image = NSImage(size: rep.size)
        image.addRepresentation(rep)
        return image
    }
}

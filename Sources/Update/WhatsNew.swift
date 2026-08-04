import AppKit
import SwiftUI

/// Post-update release-notes system (owner spec, 1.7.2, Typeless-style):
/// a PAGED card — overview first, then one page per feature, each with an
/// image slot — that auto-opens once per version and can always be reopened
/// from Settings → Release notes or the version pill. The same pager also
/// hosts the GLOBAL product guide (教程), which tours the whole product,
/// not one release.
@MainActor
enum WhatsNewGate {
    private static let seenKey = "AgentIsland.whatsNewSeenVersion"

    static var currentVersion: String {
        Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "0"
    }

    /// Always fires once per version — deliberately NOT user-configurable
    /// (owner call, 1.7.2: every user walks through the release card once).
    static func maybeShow() {
        guard !AppEnvironment.isDemo,
              ProcessInfo.processInfo.environment["AGENTISLAND_UI_SCRIPT"] == nil,
              ProcessInfo.processInfo.environment["AGENTISLAND_REPORT_SNAPSHOT"] == nil
        else { return }
        guard UserDefaults.standard.string(forKey: seenKey) != currentVersion else { return }
        WhatsNewWindowController.shared.show()
    }

    static func markSeen() {
        UserDefaults.standard.set(currentVersion, forKey: seenKey)
    }
}

// MARK: - Page model

struct PagedCardPage: Identifiable {
    /// First-page treatments (owner spec): the release card opens on the
    /// VERSION NUMBER with the feature icons; the guide opens on the brand.
    enum Hero { case version, brand }

    let id = UUID()
    let symbol: String
    /// Optional bundled illustration (PNG). Drop `whatsnew-172-*.png` /
    /// `guide-*.png` into Resources and the branded placeholder yields to
    /// the real art — no code change needed when the posters land.
    let imageName: String?
    let title: String
    let body: String
    var hero: Hero?
}

/// 1.8.1 release pages — overview first, then one page per theme. Copy is
/// deliberately terse (owner call: 精简,别什么都往上写). The 1.7.2 batch
/// never shipped, so its two themes ride this card too.
enum WhatsNewContent {
    static let pages: [PagedCardPage] = [
        // Titles are STRUCTURAL summaries — "更X的Y" noun phrases, never
        // slogans (owner call ×N, 2026-07-18: "更清爽的界面"是对的,
        // "更新自己会说话"是错的).
        PagedCardPage(
            symbol: "sparkles",
            imageName: "whatsnew-181-overview",
            title: "At a glance",
            body: "A lighter background, truer alarms, reports that look back, and Grok aboard",
            hero: .version
        ),
        PagedCardPage(
            symbol: "leaf",
            imageName: "whatsnew-181-performance",
            title: "A lighter background",
            body: "Huge transcript libraries no longer pin the CPU — scans skip cold files and reuse the index, dropping a reported 300%+ to single digits"
        ),
        PagedCardPage(
            symbol: "bell.badge",
            imageName: "whatsnew-181-alarms",
            title: "Truer alarms",
            body: "A turn you watched finish counts as seen — no more stale popups after you switch apps. Desktop hosts stay quiet while frontmost, with an optional chime"
        ),
        PagedCardPage(
            symbol: "calendar.badge.clock",
            imageName: "whatsnew-181-reports",
            title: "Reports that look back",
            body: "Flip the weekly and monthly cards to any past week or month with the arrows — and cost estimates ride the new GPT-5.6 prices"
        ),
        PagedCardPage(
            symbol: "person.badge.key",
            imageName: "whatsnew-181-signin",
            title: "Sign-in that lands right",
            body: "Copy the Claude login link into whichever browser profile holds your account; auth errors now say what actually failed"
        ),
        PagedCardPage(
            symbol: "chart.pie",
            imageName: "whatsnew-181-grok",
            title: "Grok on the island",
            body: "Signed into the grok CLI? The usage page gains its weekly pool and monthly spend — automatic, and silent when absent"
        ),
        PagedCardPage(
            symbol: "scalemass",
            imageName: "whatsnew-172-accuracy",
            title: "Truer numbers",
            body: "The accounting engine drops phantom tokens — this machine now lands within ~2% of the official Codex client, and the calendar shows the official figure beside ours"
        ),
        PagedCardPage(
            symbol: "circle.lefthalf.filled",
            imageName: "whatsnew-172-interface",
            title: "A calmer interface",
            body: "Solid pie charts, stepped bars by default, a centered solo layout, and each visual mode explained right where you pick it"
        ),
        // The closing spread (owner spec, 2026-07-18): poster art, a
        // welcome-back line, and the Get-started button beneath it.
        PagedCardPage(
            symbol: "sparkles",
            imageName: "whatsnew-172-start",
            title: "Get started",
            body: "Welcome back to Agent Island"
        ),
    ]
}

/// The global product tour (教程) — the whole product, not one release.
enum GuideContent {
    static let pages: [PagedCardPage] = [
        PagedCardPage(
            symbol: "circle.hexagongrid.circle",
            imageName: nil,
            title: "A status companion for Claude Code and Codex",
            body: "A five-page tour of everything the island does.",
            hero: .brand
        ),
        // Titles are FEATURE NOUNS, one word where possible; the sentence
        // lives in the body (owner call, 1.7.2: 标题=功能名,解释放下面).
        PagedCardPage(
            symbol: "circle.hexagongrid.circle",
            imageName: "guide-status",
            title: "Monitor",
            body: "Two marks in the notch: spinning means working, a bell means it's your turn, steady red means it needs you"
        ),
        PagedCardPage(
            symbol: "gauge.with.needle",
            imageName: "guide-usage",
            title: "Usage",
            body: "Expand the island for the 5-hour and weekly windows, straight from each provider's official endpoint — plus Grok's weekly pool when its CLI is signed in. Subscribed to just one tool? Its gauges take the whole island"
        ),
        PagedCardPage(
            symbol: "calendar",
            imageName: "guide-cost",
            title: "Cost & history",
            body: "Local session logs become token counts, API value, and the year heatmap — nothing leaves your machine"
        ),
        PagedCardPage(
            symbol: "square.and.arrow.up",
            imageName: "guide-cards",
            title: "Report cards",
            body: "One click renders a shareable battle card — copy it or AirDrop it straight to your phone, and the arrows flip back to any past week or month"
        ),
        PagedCardPage(
            symbol: "paintpalette",
            imageName: "guide-personalize",
            title: "Personalization",
            body: "Visual modes, glow colors, chart styles, language — and how alarms behave while you're in the session's app — all in Settings"
        ),
    ]
}

// MARK: - Window controllers

@MainActor
final class WhatsNewWindowController: PagedCardWindowController {
    static let shared = WhatsNewWindowController(marksSeenOnClose: true)

    override func makeView() -> AnyView {
        AnyView(PagedCardView(
            headline: L10n.tr("What's new in this update"),
            versionChip: "v\(WhatsNewGate.currentVersion)",
            pages: WhatsNewContent.pages,
            onClose: { [weak self] in self?.close() }
        ))
    }
}

@MainActor
final class GuideWindowController: PagedCardWindowController {
    static let shared = GuideWindowController(marksSeenOnClose: false)

    override func makeView() -> AnyView {
        AnyView(PagedCardView(
            headline: L10n.tr("How Agent Island works"),
            versionChip: nil,
            pages: GuideContent.pages,
            onClose: { [weak self] in self?.close() }
        ))
    }
}

@MainActor
class PagedCardWindowController: NSObject, NSWindowDelegate {
    private var window: NSWindow?
    private let marksSeenOnClose: Bool

    init(marksSeenOnClose: Bool) {
        self.marksSeenOnClose = marksSeenOnClose
    }

    func makeView() -> AnyView { AnyView(EmptyView()) }

    func show() {
        if window == nil {
            let panel = PagedCardPanel(
                contentRect: NSRect(origin: .zero, size: NSSize(width: 470, height: 560)),
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
            // Open on the Space the user is looking at — never yank them.
            panel.collectionBehavior = [.moveToActiveSpace, .fullScreenAuxiliary]
            panel.delegate = self
            window = panel
        }
        window?.contentView = NSHostingView(rootView: makeView())
        mountCloseButton()
        window?.center()
        NSApp.activate(ignoringOtherApps: true)
        // Cadence A1: appear fast and quiet — a ~100ms fade, no scale-in.
        window?.alphaValue = 0
        window?.makeKeyAndOrderFront(nil)
        NSAnimationContext.runAnimationGroup { ctx in
            ctx.duration = 0.11
            window?.animator().alphaValue = 1
        }
    }

    func close() {
        // Cadence A3: dissolve in place, then clear the stage.
        guard let window else { return }
        NSAnimationContext.runAnimationGroup({ ctx in
            ctx.duration = 0.2
            window.animator().alphaValue = 0
        }, completionHandler: {
            window.close()
            window.alphaValue = 1
        })
    }

    func windowWillClose(_ notification: Notification) {
        // Closing by any path counts as seen — never nag twice per version.
        if marksSeenOnClose { WhatsNewGate.markSeen() }
    }

    /// The REAL red traffic light on the borderless card (same pattern as
    /// the report windows) — the popup must have an obvious, native way
    /// out (owner report: 开关很难找).
    private func mountCloseButton() {
        guard let contentView = window?.contentView,
              let close = NSWindow.standardWindowButton(.closeButton, for: [.titled, .closable])
        else { return }
        close.target = window
        close.action = #selector(NSWindow.close)
        contentView.addSubview(close)
        let inset: CGFloat = 12
        let yFromTop: CGFloat = 20 + inset
        let y = contentView.isFlipped
            ? yFromTop
            : contentView.bounds.height - yFromTop - close.frame.height
        close.setFrameOrigin(NSPoint(x: 20 + inset, y: y))
    }
}

private final class PagedCardPanel: NSPanel {
    override var canBecomeKey: Bool { true }
    override func cancelOperation(_ sender: Any?) { close() }
}

/// AGENTISLAND_CARD_SNAPSHOT=/dir — renders every page of both cards to
/// PNGs and exits. The cards' QA channel.
@MainActor
enum PagedCardSnapshot {
    static func writeAll(to dir: String) {
        let url = URL(fileURLWithPath: dir)
        try? FileManager.default.createDirectory(at: url, withIntermediateDirectories: true)

        func render(_ name: String, headline: String, chip: String?,
                    pages: [PagedCardPage], index: Int) {
            let view = PagedCardView(
                headline: headline, versionChip: chip, pages: pages,
                onClose: {}, initialPage: index
            )
            let renderer = ImageRenderer(content: view)
            renderer.scale = 2
            renderer.isOpaque = false
            guard let image = renderer.nsImage,
                  let tiff = image.tiffRepresentation,
                  let rep = NSBitmapImageRep(data: tiff),
                  let png = rep.representation(using: .png, properties: [:])
            else { return }
            try? png.write(to: url.appendingPathComponent("\(name)-\(index).png"))
        }

        for i in WhatsNewContent.pages.indices {
            render("whatsnew", headline: L10n.tr("What's new in this update"),
                   chip: "v\(WhatsNewGate.currentVersion)",
                   pages: WhatsNewContent.pages, index: i)
        }
        for i in GuideContent.pages.indices {
            render("guide", headline: L10n.tr("How Agent Island works"),
                   chip: nil, pages: GuideContent.pages, index: i)
        }
    }
}

// MARK: - Paged card view

struct PagedCardView: View {
    let headline: String
    let versionChip: String?
    let pages: [PagedCardPage]
    let onClose: () -> Void

    @State private var page: Int

    init(headline: String, versionChip: String?, pages: [PagedCardPage],
         onClose: @escaping () -> Void, initialPage: Int = 0) {
        self.headline = headline
        self.versionChip = versionChip
        self.pages = pages
        self.onClose = onClose
        _page = State(initialValue: min(max(0, initialPage), max(0, pages.count - 1)))
    }

    private var isLast: Bool { page == pages.count - 1 }

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            // Wordmark row — leaves the top-left corner free for the real
            // close button the controller mounts.
            HStack(spacing: 8) {
                Spacer(minLength: 30)
                Text("AGENT ISLAND")
                    .font(.system(size: 12, weight: .heavy, design: .rounded))
                    .tracking(3.0)
                    .foregroundStyle(.white.opacity(0.9))
                if let versionChip {
                    Text(versionChip)
                        .font(.system(size: 10.5, weight: .bold, design: .rounded))
                        .monospacedDigit()
                        .foregroundStyle(IslandColor.brandTeal)
                        .padding(.horizontal, 7)
                        .padding(.vertical, 2)
                        .background(Capsule().fill(IslandColor.brandTeal.opacity(0.12)))
                }
                Spacer(minLength: 30)
            }
            .padding(.bottom, 16)

            let current = pages[page]

            // Cadence B5: page content cross-fades in place (no slide).
            VStack(alignment: .leading, spacing: 0) {
                Group {
                    switch current.hero {
                    case .version: VersionHero(page: current, siblings: pages)
                    case .brand:   BrandHero(siblings: pages)
                    case nil:      PageIllustration(page: current)
                    }
                }
                .padding(.bottom, 18)

                Text(L10n.tr(current.title))
                    .font(.system(size: 20, weight: .black, design: .rounded))
                    .foregroundStyle(.white)
                    .padding(.bottom, 8)

                Text(L10n.tr(current.body))
                    .font(.system(size: 12.5, weight: .medium, design: .rounded))
                    .foregroundStyle(.white.opacity(0.62))
                    .lineSpacing(3)
                    .fixedSize(horizontal: false, vertical: true)
            }
            .id(page)
            .transition(.opacity)

            Spacer(minLength: 16)

            HStack(spacing: 10) {
                // Page dots — the "there is more" cue the first cut lacked.
                HStack(spacing: 5) {
                    ForEach(pages.indices, id: \.self) { i in
                        // Tappable; the active dot stretches into a pill
                        // (small joy, Cadence school).
                        Capsule()
                            .fill(i == page ? IslandColor.brandTeal : Color.white.opacity(0.16))
                            .frame(width: i == page ? 16 : 6, height: 6)
                            .contentShape(Rectangle().inset(by: -4))
                            .onTapGesture {
                                Haptics.tap()
                                withAnimation(.spring(response: 0.32, dampingFraction: 0.8)) {
                                    page = i
                                }
                            }
                    }
                }
                Spacer()
                if page > 0 {
                    Button {
                        withAnimation(.easeInOut(duration: 0.28)) { page -= 1 }
                    } label: {
                        Text(L10n.tr("Back"))
                            .font(.system(size: 12, weight: .bold, design: .rounded))
                            .foregroundStyle(.white.opacity(0.55))
                            .padding(.horizontal, 14)
                            .frame(height: 34)
                            .background(
                                RoundedRectangle(cornerRadius: 10, style: .continuous)
                                    .fill(Color.white.opacity(0.05))
                            )
                    }
                    .buttonStyle(TactileButtonStyle())
                }
                Button {
                    if isLast {
                        onClose()
                    } else {
                        withAnimation(.easeInOut(duration: 0.28)) { page += 1 }
                    }
                } label: {
                    Text(isLast ? L10n.tr("Get started") : L10n.tr("Next"))
                        .font(.system(size: 12.5, weight: .heavy, design: .rounded))
                        .foregroundStyle(.black.opacity(0.9))
                        .padding(.horizontal, 18)
                        .frame(height: 34)
                        .background(
                            RoundedRectangle(cornerRadius: 10, style: .continuous)
                                .fill(IslandColor.brandTeal)
                        )
                }
                .buttonStyle(TactileButtonStyle())
            }
        }
        .padding(24)
        .frame(width: 470, height: 560)
        .background(
            RoundedRectangle(cornerRadius: 26, style: .continuous)
                .fill(WeeklyReportCard.baseCoat)
                .overlay(
                    RoundedRectangle(cornerRadius: 26, style: .continuous)
                        .strokeBorder(Color.white.opacity(0.06), lineWidth: 1)
                )
        )
        .shadow(color: .black.opacity(0.35), radius: 18, y: 8)
        .padding(16)
    }
}

/// The release card's opening spread: the version number IS the visual,
/// the feature icons preview the pages, the real screenshot grounds it.
private struct VersionHero: View {
    let page: PagedCardPage
    let siblings: [PagedCardPage]

    var body: some View {
        VStack(spacing: 14) {
            Text("v" + WhatsNewGate.currentVersion)
                .font(.system(size: 42, weight: .black, design: .rounded))
                .monospacedDigit()
                .foregroundStyle(.white)
            IconRow(symbols: siblings.filter { $0.hero == nil }.map(\.symbol))
            PageIllustration(page: page, height: 138)
        }
        .frame(maxWidth: .infinity)
    }
}

/// The guide's opening spread: poster-grade — the island artwork behind
/// the brand, darkened just enough to let the wordmark own the frame
/// (owner spec, 2026-07-18: 教程也要海报级设计感).
private struct BrandHero: View {
    let siblings: [PagedCardPage]

    private var logo: NSImage? {
        Bundle.main.url(forResource: "agentisland_logo", withExtension: "png")
            .flatMap { NSImage(contentsOf: $0) }
    }

    private var posterBackdrop: NSImage? {
        Bundle.main.url(forResource: "guide-brand", withExtension: "png")
            .flatMap { NSImage(contentsOf: $0) }
    }

    var body: some View {
        Color.clear
            .frame(height: 240)
            .frame(maxWidth: .infinity)
            .overlay {
                if let posterBackdrop {
                    ZStack {
                        Image(nsImage: posterBackdrop)
                            .resizable()
                            .interpolation(.high)
                            .aspectRatio(contentMode: .fill)
                        LinearGradient(
                            colors: [.black.opacity(0.18), .black.opacity(0.52)],
                            startPoint: .top, endPoint: .bottom
                        )
                    }
                }
            }
            .overlay {
                VStack(spacing: 12) {
                    if let logo {
                        Image(nsImage: logo)
                            .resizable()
                            .interpolation(.high)
                            .aspectRatio(contentMode: .fit)
                            .frame(width: 58, height: 58)
                            .shadow(color: .black.opacity(0.5), radius: 8)
                    }
                    Text("Agent Island")
                        .font(.system(size: 25, weight: .black, design: .rounded))
                        .foregroundStyle(.white)
                        .shadow(color: .black.opacity(0.6), radius: 6)
                    IconRow(symbols: siblings.filter { $0.hero == nil }.map(\.symbol))
                }
            }
            .clipShape(RoundedRectangle(cornerRadius: 16, style: .continuous))
            .overlay(
                RoundedRectangle(cornerRadius: 16, style: .continuous)
                    .strokeBorder(Color.white.opacity(0.07), lineWidth: 1)
            )
    }
}

private struct IconRow: View {
    let symbols: [String]
    @State private var appeared = false

    var body: some View {
        HStack(spacing: 10) {
            ForEach(Array(symbols.enumerated()), id: \.offset) { i, symbol in
                Image(systemName: symbol)
                    .font(.system(size: 13, weight: .semibold))
                    .foregroundStyle(IslandColor.brandTeal)
                    .frame(width: 30, height: 30)
                    .background(
                        RoundedRectangle(cornerRadius: 9, style: .continuous)
                            .fill(IslandColor.brandTeal.opacity(0.10))
                            .overlay(
                                RoundedRectangle(cornerRadius: 9, style: .continuous)
                                    .strokeBorder(IslandColor.brandTeal.opacity(0.22), lineWidth: 1)
                            )
                    )
                    // Staggered rise-in — the Cadence chip-flight cadence,
                    // minus the flight.
                    .opacity(appeared ? 1 : 0)
                    .offset(y: appeared ? 0 : 7)
                    .animation(
                        .spring(response: 0.4, dampingFraction: 0.8).delay(Double(i) * 0.07),
                        value: appeared
                    )
            }
        }
        .onAppear { appeared = true }
    }
}

/// Bundled REAL screenshot if present (owner call: 真实截图,不要示意图);
/// otherwise a brand-toned placeholder that still looks intentional. Very
/// wide captures (the menu-bar strip) letterbox on the coat instead of
/// being zoom-cropped into abstraction.
private struct PageIllustration: View {
    let page: PagedCardPage
    var height: CGFloat = 240

    private var poster: NSImage? {
        guard let imageName = page.imageName else { return nil }
        // English UI prefers the -en poster when one exists; zh art is the
        // fallback so a missing translation never blanks the slot.
        if !L10n.locale.identifier.hasPrefix("zh"),
           let en = Bundle.main.url(forResource: imageName + "-en", withExtension: "png")
               .flatMap({ NSImage(contentsOf: $0) }) {
            return en
        }
        return Bundle.main.url(forResource: imageName, withExtension: "png")
            .flatMap { NSImage(contentsOf: $0) }
    }

    var body: some View {
        // The illustration NEVER participates in layout sizing: a full-res
        // screenshot's intrinsic width once inflated the whole card column
        // to 1600+pt and shoved the title off the canvas (owner screenshot,
        // 2026-07-18). `Color.clear` owns the layout; the art rides an
        // overlay and gets clipped.
        Color.clear
            .frame(height: height)
            .frame(maxWidth: .infinity)
            .overlay {
                if let poster {
                    let ratio = poster.size.height > 0 ? poster.size.width / poster.size.height : 1
                    ZStack {
                        Color.white.opacity(0.03)
                        Image(nsImage: poster)
                            .resizable()
                            .interpolation(.high)
                            .aspectRatio(contentMode: ratio > 2.2 ? .fit : .fill)
                            .padding(ratio > 2.2 ? 14 : 0)
                    }
                } else {
                    ZStack {
                        LinearGradient(
                            colors: [
                                IslandColor.brandTeal.opacity(0.16),
                                IslandColor.cobalt.opacity(0.10),
                                Color.white.opacity(0.02),
                            ],
                            startPoint: .topLeading, endPoint: .bottomTrailing
                        )
                        Image(systemName: page.symbol)
                            .font(.system(size: 46, weight: .medium))
                            .foregroundStyle(IslandColor.brandTeal.opacity(0.85))
                    }
                }
            }
            .clipShape(RoundedRectangle(cornerRadius: 16, style: .continuous))
            .overlay(
                RoundedRectangle(cornerRadius: 16, style: .continuous)
                    .strokeBorder(Color.white.opacity(0.07), lineWidth: 1)
            )
    }
}

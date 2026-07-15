import SwiftUI
import AppKit
import CoreImage

/// The monthly share card — the weekly card's big sibling. Where the weekly
/// tells "this week's burn", the monthly tells the LONG story: this month's
/// totals up top, and a 24-week activity heatmap (the streak brag) as the
/// centerpiece. Same no-upload rules: rendered from local logs only.
struct MonthlyReportData {
    let monthText: String          // "2026年7月" / "July 2026"
    let totalTokens: Int           // calendar month to date, wire
    let totalDollars: Double
    let claudeShare: Double
    /// Heat levels per column (oldest → newest), 7 rows Mon→Sun.
    /// -1 = outside history/future, 0...4 = intensity.
    let heat: [[Int]]
    let weeksCount: Int
    let streakDays: Int
    let activeDays: Int
    let peakText: String           // e.g. "35.7亿" / "3.57B"
    let milestoneText: String?     // "🏆 百亿俱乐部 · 累计 227 亿 Token"

    @MainActor
    static func current() -> MonthlyReportData {
        let cost = CostStore.shared
        let cal = Calendar.current
        let today = cal.startOfDay(for: Date())
        let zh = L10n.locale.identifier.hasPrefix("zh")

        // Merge both providers' daily buckets into one map.
        var daily: [Date: Int] = [:]
        for bucket in cost.claude.dailyTokens + cost.codex.dailyTokens {
            daily[cal.startOfDay(for: bucket.dayStart), default: 0] += bucket.tokens
        }
        let firstDataDay = daily.keys.min() ?? today

        // Month header numbers ride the existing month window (wire tokens).
        let totalTokens = cost.claude.month.tokens + cost.codex.month.tokens
        let totalDollars = cost.claude.month.dollars + cost.codex.month.dollars
        let claudeShare = totalTokens > 0
            ? Double(cost.claude.month.tokens) / Double(totalTokens) : 0

        // 24 heat weeks ending with the current (Monday-started) week.
        let weeks = 24
        let sinceMonday = (cal.component(.weekday, from: today) + 5) % 7
        let thisMonday = cal.date(byAdding: .day, value: -sinceMonday, to: today) ?? today
        let gridStart = cal.date(byAdding: .day, value: -7 * (weeks - 1), to: thisMonday) ?? thisMonday

        // Quartile levels over the window's nonzero days (GitHub-style) —
        // a linear scale would flatline everything next to a 3.5B peak day.
        var windowValues: [Int] = []
        var day = gridStart
        while day <= today {
            let v = daily[day, default: 0]
            if v > 0 { windowValues.append(v) }
            day = cal.date(byAdding: .day, value: 1, to: day) ?? today.addingTimeInterval(1)
            if day > today { break }
        }
        let sorted = windowValues.sorted()
        func quartile(_ q: Double) -> Int {
            guard !sorted.isEmpty else { return 1 }
            return sorted[min(sorted.count - 1, Int(q * Double(sorted.count)))]
        }
        let q1 = quartile(0.25), q2 = quartile(0.5), q3 = quartile(0.75)
        func level(_ v: Int) -> Int {
            if v <= 0 { return 0 }
            if v < q1 { return 1 }
            if v < q2 { return 2 }
            if v < q3 { return 3 }
            return 4
        }

        var heat: [[Int]] = []
        for col in 0..<weeks {
            var column: [Int] = []
            for row in 0..<7 {
                guard let d = cal.date(byAdding: .day, value: col * 7 + row, to: gridStart) else {
                    column.append(-1)
                    continue
                }
                if d > today || d < firstDataDay {
                    column.append(-1)
                } else {
                    column.append(level(daily[d, default: 0]))
                }
            }
            heat.append(column)
        }

        // Current streak: consecutive active days ending today (or yesterday
        // when today hasn't logged anything yet — don't zero the brag at 9am).
        var streak = 0
        var cursor = daily[today, default: 0] > 0
            ? today
            : cal.date(byAdding: .day, value: -1, to: today) ?? today
        while daily[cursor, default: 0] > 0 {
            streak += 1
            guard let prev = cal.date(byAdding: .day, value: -1, to: cursor) else { break }
            cursor = prev
        }

        let activeDays = windowValues.count
        let peak = windowValues.max() ?? 0

        let df = DateFormatter()
        df.locale = zh ? Locale(identifier: "zh_CN") : Locale(identifier: "en_US_POSIX")
        df.dateFormat = zh ? "yyyy年M月" : "MMMM yyyy"

        let lifetime = daily.values.reduce(0, +)
        let milestoneText = MilestoneLadder.tokenTier(lifetime: lifetime).map { tier in
            tier.emoji + " " + L10n.tr("%@ rank · lifetime %@ tokens",
                                       L10n.tr(tier.nameKey),
                                       WeeklyReportCard.compactString(lifetime, zh: zh))
        }

        return MonthlyReportData(
            monthText: df.string(from: today),
            totalTokens: totalTokens,
            totalDollars: totalDollars,
            claudeShare: claudeShare,
            heat: heat,
            weeksCount: weeks,
            streakDays: streak,
            activeDays: activeDays,
            peakText: WeeklyReportCard.compactString(peak, zh: zh),
            milestoneText: milestoneText
        )
    }
}

struct MonthlyReportCard: View {
    let data: MonthlyReportData
    var rounded: Bool = true

    static let size = CGSize(width: 420, height: 560)

    // Brand-teal heat ramp (report card v2: palette follows the theme).
    private static let ramp: [Color] = [
        Color.white.opacity(0.06),
        Color(red: 0.043, green: 0.184, blue: 0.165),
        Color(red: 0.055, green: 0.329, blue: 0.294),
        Color(red: 0.075, green: 0.529, blue: 0.478),
        Color(red: 0.125, green: 0.753, blue: 0.690),
    ]

    var body: some View {
        ZStack {
            background

            VStack(alignment: .leading, spacing: 0) {
                header
                Spacer(minLength: 14)
                hero
                Spacer(minLength: 16)
                providerSplit
                Spacer(minLength: 18)
                heatBlock
                Spacer(minLength: 16)
                footer
            }
            .padding(30)
        }
        .frame(width: Self.size.width, height: Self.size.height)
        .clipShape(RoundedRectangle(cornerRadius: rounded ? CardWindow.cornerRadius : 0, style: .continuous))
    }

    private var background: some View {
        ZStack {
            RoundedRectangle(cornerRadius: rounded ? CardWindow.cornerRadius : 0, style: .continuous)
                .fill(
                    LinearGradient(
                        colors: [Color(red: 0.075, green: 0.08, blue: 0.09),
                                 Color(red: 0.028, green: 0.03, blue: 0.038)],
                        startPoint: .topLeading, endPoint: .bottomTrailing
                    )
                )
            // Monthly leans blue — the heatmap is the hero here.
            RadialGradient(colors: [IslandColor.codex.opacity(0.14), .clear],
                           center: .init(x: 0.9, y: 0.06), startRadius: 0, endRadius: 360)
            RadialGradient(colors: [IslandColor.claude.opacity(0.09), .clear],
                           center: .init(x: 0.06, y: 0.9), startRadius: 0, endRadius: 340)
            RoundedRectangle(cornerRadius: rounded ? CardWindow.cornerRadius : 0, style: .continuous)
                .strokeBorder(
                    LinearGradient(colors: [.white.opacity(0.16), .white.opacity(0.02)],
                                   startPoint: .top, endPoint: .bottom),
                    lineWidth: 1
                )
        }
    }

    private var header: some View {
        HStack(alignment: .firstTextBaseline) {
            Text("AGENT ISLAND")
                .font(.system(size: 11, weight: .heavy, design: .rounded))
                .tracking(3.2)
                .foregroundStyle(.white.opacity(0.85))
            Text("MONTHLY")
                .font(.system(size: 11, weight: .heavy, design: .rounded))
                .tracking(3.2)
                .foregroundStyle(
                    LinearGradient(colors: [IslandColor.brandTeal, Color(red: 0.49, green: 0.94, blue: 0.89)],
                                   startPoint: .leading, endPoint: .trailing)
                )
            Spacer()
            Text(data.monthText)
                .font(.system(size: 10.5, weight: .semibold, design: .monospaced))
                .foregroundStyle(.white.opacity(0.42))
        }
    }

    private var hero: some View {
        let zh = L10n.locale.identifier.hasPrefix("zh")
        let parts = WeeklyReportCard.compactParts(data.totalTokens, zh: zh)
        return VStack(alignment: .leading, spacing: 8) {
            Text(L10n.tr("tokens this month"))
                .font(.system(size: 14, weight: .bold, design: .rounded))
                .tracking(0.4)
                .foregroundStyle(.white.opacity(0.55))
            HStack(alignment: .firstTextBaseline, spacing: 2) {
                Text(parts.0)
                    .font(.system(size: 74, weight: .heavy, design: .rounded))
                    .kerning(-1.5)
                if !parts.1.isEmpty {
                    Text(parts.1)
                        .font(.system(size: zh ? 38 : 74, weight: .heavy, design: .rounded))
                }
            }
            .foregroundStyle(
                LinearGradient(colors: [.white, .white.opacity(0.72)],
                               startPoint: .top, endPoint: .bottom)
            )
            if data.totalDollars >= 1 {
                Text(L10n.tr("≈ $%@ API value", WeeklyReportCard.money(data.totalDollars)))
                    .font(.system(size: 13.5, weight: .heavy, design: .rounded))
                    .foregroundStyle(IslandColor.liveTeal)
            }
        }
    }

    private var providerSplit: some View {
        VStack(alignment: .leading, spacing: 10) {
            GeometryReader { geo in
                HStack(spacing: 2) {
                    Capsule().fill(IslandColor.claude)
                        .frame(width: max(4, geo.size.width * data.claudeShare))
                    Capsule().fill(IslandColor.codex)
                }
            }
            .frame(height: 7)
            HStack(spacing: 18) {
                providerTag(name: "Claude",
                            pct: data.claudeShare, color: IslandColor.claude)
                providerTag(name: "Codex",
                            pct: 1 - data.claudeShare, color: IslandColor.codex)
                Spacer()
            }
        }
    }

    private func providerTag(name: String, pct: Double, color: Color) -> some View {
        HStack(spacing: 6) {
            // One logo per card (the brand's, in the footer) — providers get
            // color dots, not marks (owner: 只能出现一个 logo).
            Circle()
                .fill(color)
                .frame(width: 7, height: 7)
            Text(name)
                .font(.system(size: 11.5, weight: .bold, design: .rounded))
                .foregroundStyle(.white.opacity(0.78))
            Text("\(Int((pct * 100).rounded()))%")
                .font(.system(size: 11.5, weight: .heavy, design: .rounded))
                .foregroundStyle(color)
        }
    }

    // MARK: - Heatmap centerpiece

    private var heatBlock: some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack(alignment: .firstTextBaseline) {
                Text(L10n.tr("Past %d weeks", data.weeksCount))
                    .font(.system(size: 11.5, weight: .bold, design: .rounded))
                    .foregroundStyle(.white.opacity(0.55))
                Spacer()
                if data.streakDays >= 2 {
                    Text(L10n.tr("%d-day streak 🔥", data.streakDays))
                        .font(.system(size: 12.5, weight: .heavy, design: .rounded))
                        .foregroundStyle(Color(red: 1.0, green: 0.62, blue: 0.35))
                }
            }

            HStack(spacing: 2.5) {
                ForEach(Array(data.heat.enumerated()), id: \.offset) { _, column in
                    VStack(spacing: 2.5) {
                        ForEach(Array(column.enumerated()), id: \.offset) { _, lvl in
                            RoundedRectangle(cornerRadius: 2.5, style: .continuous)
                                .fill(lvl < 0 ? Color.clear : Self.ramp[lvl])
                                .frame(width: 11, height: 11)
                                .shadow(color: lvl == 4 ? Self.ramp[4].opacity(0.5) : .clear,
                                        radius: 3)
                        }
                    }
                }
            }

            HStack(spacing: 4) {
                Text(L10n.tr("Active %d days · peak %@", data.activeDays, data.peakText))
                    .font(.system(size: 10, weight: .bold, design: .rounded))
                    .foregroundStyle(.white.opacity(0.42))
                Spacer()
                Text(L10n.tr("less"))
                    .font(.system(size: 9, weight: .bold, design: .rounded))
                    .foregroundStyle(.white.opacity(0.35))
                ForEach(0..<5, id: \.self) { i in
                    RoundedRectangle(cornerRadius: 2)
                        .fill(Self.ramp[i])
                        .frame(width: 8, height: 8)
                }
                Text(L10n.tr("more"))
                    .font(.system(size: 9, weight: .bold, design: .rounded))
                    .foregroundStyle(.white.opacity(0.35))
            }
        }
    }

    private var footer: some View {
        VStack(spacing: 12) {
            if let milestone = data.milestoneText {
                Text(milestone)
                    .font(.system(size: 11, weight: .heavy, design: .rounded))
                    .foregroundStyle(Color(red: 1.0, green: 0.78, blue: 0.42))
                    .frame(maxWidth: .infinity, alignment: .leading)
            }
            Rectangle()
                .fill(LinearGradient(colors: [.white.opacity(0.0), .white.opacity(0.14), .white.opacity(0.0)],
                                     startPoint: .leading, endPoint: .trailing))
                .frame(height: 1)
            // Share-clean strip: no QR, no URL (they read as ads on social
            // feeds) — one logo, one name, centered.
            HStack(alignment: .center, spacing: 10) {
                Spacer()
                if let icon = NSImage(named: NSImage.applicationIconName) {
                    Image(nsImage: icon)
                        .resizable()
                        .aspectRatio(contentMode: .fit)
                        .frame(width: 30, height: 30)
                }
                Text("Agent Island")
                    .font(.system(size: 13, weight: .heavy, design: .rounded))
                    .foregroundStyle(.white.opacity(0.88))
                Spacer()
            }
        }
    }

}

// MARK: - Renderer + window (mirrors the weekly pair)

@MainActor
enum MonthlyReportRenderer {
    private static var cachedImage: NSImage?
    private static var cachedPNG: Data?

    static func invalidateCache() {
        cachedImage = nil
        cachedPNG = nil
    }

    static func warmCache() {
        _ = pngData()
    }

    static func image() -> NSImage? {
        if let cachedImage { return cachedImage }
        let renderer = ImageRenderer(content: MonthlyReportCard(data: .current(), rounded: false))
        renderer.scale = 3
        renderer.isOpaque = true
        cachedImage = renderer.nsImage
        return cachedImage
    }

    static func pngData() -> Data? {
        if let cachedPNG { return cachedPNG }
        guard let image = image(),
              let tiff = image.tiffRepresentation,
              let rep = NSBitmapImageRep(data: tiff) else { return nil }
        cachedPNG = rep.representation(using: .png, properties: [:])
        return cachedPNG
    }

    static func writePNG(to path: String) {
        guard let data = pngData() else {
            NSLog("AgentIsland monthly report: render failed")
            return
        }
        try? data.write(to: URL(fileURLWithPath: path))
    }
}

private final class MonthlyPanel: NSPanel {
    override var canBecomeKey: Bool { true }
    override func cancelOperation(_ sender: Any?) { close() }
}

@MainActor
final class MonthlyReportWindowController: NSWindowController, NSWindowDelegate {
    static let shared = MonthlyReportWindowController()

    private init() {
        super.init(window: nil)
    }

    @available(*, unavailable)
    required init?(coder: NSCoder) { fatalError() }

    func show() {
        MonthlyReportRenderer.invalidateCache()
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.2) {
            MonthlyReportRenderer.warmCache()
        }
        if window == nil {
            let panel = MonthlyPanel(
                contentRect: NSRect(origin: .zero, size: NSSize(width: 472, height: 670)),
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
        // Rebuilt EVERY show — a cached SwiftUI tree kept serving stale data
        // and the pre-switch language (the "English UI, Chinese poster" bug).
        window?.contentView = NSHostingView(rootView: MonthlyReportSheet())
        mountCloseButton()
        window?.center()
        NSApp.activate(ignoringOtherApps: true)
        window?.makeKeyAndOrderFront(nil)
    }

    private func mountCloseButton() {
        guard let contentView = window?.contentView,
              let close = NSWindow.standardWindowButton(.closeButton, for: [.titled, .closable])
        else { return }
        close.target = window
        close.action = #selector(NSWindow.close)
        contentView.addSubview(close)
        let inset: CGFloat = 12
        let yFromTop: CGFloat = 22 + inset
        let y = contentView.isFlipped
            ? yFromTop
            : contentView.bounds.height - yFromTop - close.frame.height
        close.setFrameOrigin(NSPoint(x: 26 + inset, y: y))
    }
}

@MainActor
private struct MonthlyReportSheet: View {
    @State private var copied = false
    @State private var coach: String?
    @State private var shareAnchor: NSView?
    @State private var pickerHolder = MonthlyPickerHolder()

    var body: some View {
        VStack(spacing: 14) {
            MonthlyReportCard(data: .current())
                .shadow(color: .black.opacity(0.30), radius: 10, y: 4)

            HStack(spacing: 10) {
                pill(copied ? L10n.tr("Copied") : L10n.tr("Copy image")) {
                    if let image = MonthlyReportRenderer.image() {
                        NSPasteboard.general.clearContents()
                        NSPasteboard.general.writeObjects([image])
                        copied = true
                        showCoach(L10n.tr("Copied! Post it and bring a friend to the island 🏝️ Thanks for spreading the word"))
                        DispatchQueue.main.asyncAfter(deadline: .now() + 1.6) { copied = false }
                    }
                }
                pill(L10n.tr("Share…")) {
                    showCoach(L10n.tr("Tip: AirDrop it to your iPhone — it lands in Photos, ready to post 📲"))
                    openSharePicker()
                }
                .background(
                    MonthlyShareAnchorView { shareAnchor = $0 }
                        .frame(width: 1, height: 1)
                )
            }

            Text(coach ?? " ")
                .font(.system(size: 11, weight: .bold, design: .rounded))
                .foregroundStyle(IslandColor.liveTeal)
                .lineLimit(1)
                .opacity(coach == nil ? 0 : 1)
                .animation(.easeOut(duration: 0.2), value: coach == nil)
        }
        .padding(.horizontal, 26)
        .padding(.top, 22)
        .padding(.bottom, 12)
    }

    private func showCoach(_ text: String) {
        coach = text
        DispatchQueue.main.asyncAfter(deadline: .now() + 8) {
            if coach == text { coach = nil }
        }
    }

    @MainActor
    private func openSharePicker() {
        guard let image = MonthlyReportRenderer.image(), let anchor = shareAnchor else { return }
        let picker = NSSharingServicePicker(items: [image])
        pickerHolder.picker = picker
        picker.show(relativeTo: anchor.bounds, of: anchor, preferredEdge: .minY)
    }

    private func pill(_ title: String, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            Text(title)
                .font(.system(size: 12, weight: .bold, design: .rounded))
                .foregroundStyle(.black)
                .padding(.horizontal, 16)
                .frame(height: 30)
                .background(Capsule().fill(Color.white))
        }
        .buttonStyle(.plain)
    }
}

private struct MonthlyShareAnchorView: NSViewRepresentable {
    let onReady: (NSView) -> Void

    func makeNSView(context: Context) -> NSView {
        let view = NSView(frame: .zero)
        DispatchQueue.main.async { onReady(view) }
        return view
    }

    func updateNSView(_ nsView: NSView, context: Context) {}
}

// Plain retain box (no actor isolation): @MainActor here broke CI —
// a @State default value is initialized in a nonisolated context, and
// newer compilers reject the implicit hop. The picker itself is only
// ever touched from the view body (main thread) anyway.
private final class MonthlyPickerHolder {
    var picker: NSSharingServicePicker?
}

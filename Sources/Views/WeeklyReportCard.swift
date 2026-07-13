import SwiftUI
import AppKit
import CoreImage

/// The shareable weekly report — a fixed-size portrait card rendered from
/// LOCAL data only (CostStore's log scan + UsageStore quota). Users copy or
/// save it as a PNG and post it themselves; nothing is ever uploaded, which
/// is what lets this exist at all under the no-telemetry promise.
///
/// Texture rules (the owner's bar: 质感第一): layered dark gradients, one
/// hairline top-light border, faint provider-colored glows, no flat chips,
/// no borders-around-everything.
struct WeeklyReportData {
    struct ModelShare: Identifiable {
        let id = UUID()
        let name: String
        let percent: Double   // 0...1 of the combined week
        let color: Color
    }

    let rangeText: String
    let totalTokens: Int
    let totalDollars: Double
    let claudeShare: Double   // 0...1 of weekly tokens
    let dailyTokens: [Int]    // oldest → today, exactly 7
    let dayLetters: [String]
    let topModels: [ModelShare]

    /// Assembles the last 7 calendar days from CostStore. All local.
    @MainActor
    static func current() -> WeeklyReportData {
        let cost = CostStore.shared
        let cal = Calendar.current
        let today = cal.startOfDay(for: Date())
        let days: [Date] = (0..<7).reversed().compactMap {
            cal.date(byAdding: .day, value: -$0, to: today)
        }

        func bucketTotal(_ buckets: [DailyTokenBucket], _ day: Date) -> Int {
            buckets.first(where: { cal.isDate($0.dayStart, inSameDayAs: day) })?.tokens ?? 0
        }
        let claudeDaily = days.map { bucketTotal(cost.claude.dailyTokens, $0) }
        let codexDaily = days.map { bucketTotal(cost.codex.dailyTokens, $0) }
        let daily = zip(claudeDaily, codexDaily).map(+)

        let claudeWeek = claudeDaily.reduce(0, +)
        let codexWeek = codexDaily.reduce(0, +)
        let total = claudeWeek + codexWeek

        let dollars = (cost.claude.weekByModel + cost.codex.weekByModel)
            .reduce(0.0) { $0 + $1.dollars }

        // Rank models by DOLLARS, not billable tokens: the card's story is
        // "what my week was worth", and token-ranking buried expensive
        // models — Fable 5 ($10/$50 rates) ranked below cheaper models that
        // pushed more tokens and got cut from the list entirely.
        let dollarUniverse = max(0.01, (cost.claude.weekByModel + cost.codex.weekByModel)
            .reduce(0.0) { $0 + $1.dollars })
        let claudeRows = cost.claude.weekByModel.map {
            ModelShare(name: $0.displayName, percent: $0.dollars / dollarUniverse, color: IslandColor.claude)
        }
        let codexRows = cost.codex.weekByModel.map {
            ModelShare(name: $0.displayName, percent: $0.dollars / dollarUniverse, color: IslandColor.codex)
        }
        var models = (claudeRows + codexRows).sorted { $0.percent > $1.percent }
        // No "0%" tail rows — a model must have earned at least half a
        // percent of the week's spend to make the card.
        models = Array(models.filter { $0.percent >= 0.005 }.prefix(4))

        // The card follows the app language — a card destined for WeChat
        // groups must read Chinese when the UI is Chinese.
        let zh = L10n.locale.identifier.hasPrefix("zh")
        let df = DateFormatter()
        df.locale = zh ? Locale(identifier: "zh_CN") : Locale(identifier: "en_US_POSIX")
        df.dateFormat = zh ? "M月d日" : "MMM d"
        let range = "\(df.string(from: days.first ?? today)) – \(df.string(from: today))"

        let letters: [String]
        if zh {
            let zhDays = ["日", "一", "二", "三", "四", "五", "六"]
            letters = days.map { zhDays[cal.component(.weekday, from: $0) - 1] }
        } else {
            let letterFmt = DateFormatter()
            letterFmt.locale = Locale(identifier: "en_US_POSIX")
            letterFmt.dateFormat = "EEEEE"
            letters = days.map { letterFmt.string(from: $0) }
        }

        return WeeklyReportData(
            rangeText: range,
            totalTokens: total,
            totalDollars: dollars,
            claudeShare: total > 0 ? Double(claudeWeek) / Double(total) : 0,
            dailyTokens: daily,
            dayLetters: letters,
            topModels: models
        )
    }
}

struct WeeklyReportCard: View {
    let data: WeeklyReportData
    /// The in-window card is rounded; the EXPORTED card is square-cornered
    /// and full-bleed — social apps flatten transparency to white, so any
    /// rounded transparent corner pastes as ugly white nicks.
    var rounded: Bool = true

    static let size = CGSize(width: 420, height: 560)

    var body: some View {
        ZStack {
            background

            VStack(alignment: .leading, spacing: 0) {
                header
                Spacer(minLength: 18)
                hero
                Spacer(minLength: 22)
                providerSplit
                Spacer(minLength: 24)
                weekBars
                Spacer(minLength: 24)
                modelRows
                Spacer(minLength: 20)
                footer // brand strip — doubles as the future sponsor slot
            }
            .padding(30)
        }
        .frame(width: Self.size.width, height: Self.size.height)
        .clipShape(RoundedRectangle(cornerRadius: rounded ? 26 : 0, style: .continuous))
    }

    // MARK: - Texture

    private var background: some View {
        ZStack {
            RoundedRectangle(cornerRadius: rounded ? 26 : 0, style: .continuous)
                .fill(
                    LinearGradient(
                        colors: [Color(red: 0.075, green: 0.08, blue: 0.09),
                                 Color(red: 0.028, green: 0.03, blue: 0.038)],
                        startPoint: .topLeading, endPoint: .bottomTrailing
                    )
                )
            // Faint provider auras — enough to feel alive, never loud.
            RadialGradient(colors: [IslandColor.claude.opacity(0.13), .clear],
                           center: .init(x: 0.12, y: 0.02), startRadius: 0, endRadius: 340)
            RadialGradient(colors: [IslandColor.codex.opacity(0.11), .clear],
                           center: .init(x: 0.95, y: 0.85), startRadius: 0, endRadius: 380)
            RoundedRectangle(cornerRadius: rounded ? 26 : 0, style: .continuous)
                .strokeBorder(
                    LinearGradient(colors: [.white.opacity(0.16), .white.opacity(0.02)],
                                   startPoint: .top, endPoint: .bottom),
                    lineWidth: 1
                )
        }
    }

    // MARK: - Sections

    private var header: some View {
        HStack(alignment: .firstTextBaseline) {
            Text("AGENT ISLAND")
                .font(.system(size: 11, weight: .heavy, design: .rounded))
                .tracking(3.2)
                .foregroundStyle(.white.opacity(0.85))
            Text("WEEKLY")
                .font(.system(size: 11, weight: .heavy, design: .rounded))
                .tracking(3.2)
                .foregroundStyle(
                    LinearGradient(colors: [IslandColor.claude, IslandColor.codex],
                                   startPoint: .leading, endPoint: .trailing)
                )
            Spacer()
            Text(data.rangeText)
                .font(.system(size: 10.5, weight: .semibold, design: .monospaced))
                .foregroundStyle(.white.opacity(0.42))
        }
    }

    private var hero: some View {
        let zh = L10n.locale.identifier.hasPrefix("zh")
        let parts = Self.compactParts(data.totalTokens, zh: zh)
        return VStack(alignment: .leading, spacing: 7) {
            HStack(alignment: .firstTextBaseline, spacing: 2) {
                Text(parts.0)
                    .font(.system(size: 74, weight: .heavy, design: .rounded))
                    .kerning(-1.5)
                if !parts.1.isEmpty {
                    // 中文单位(亿/万)按惯例小一号挂在数字后;英文单位(B/M)
                    // 与数字同体量。
                    Text(parts.1)
                        .font(.system(size: zh ? 38 : 74, weight: .heavy, design: .rounded))
                }
            }
            .foregroundStyle(
                LinearGradient(colors: [.white, .white.opacity(0.72)],
                               startPoint: .top, endPoint: .bottom)
            )
            HStack(spacing: 8) {
                Text(L10n.tr("tokens this week"))
                    .font(.system(size: 13.5, weight: .semibold, design: .rounded))
                    .foregroundStyle(.white.opacity(0.5))
                if data.totalDollars >= 1 {
                    Text(L10n.tr("≈ $%@ API value", Self.money(data.totalDollars)))
                        .font(.system(size: 13.5, weight: .heavy, design: .rounded))
                        .foregroundStyle(Color(red: 0.55, green: 0.85, blue: 0.62))
                }
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
                providerTag(logo: ProviderLogos.claude, name: "Claude",
                            pct: data.claudeShare, color: IslandColor.claude)
                providerTag(logo: ProviderLogos.openAI, name: "Codex",
                            pct: 1 - data.claudeShare, color: IslandColor.codex)
                Spacer()
            }
        }
    }

    private func providerTag(logo: NSImage?, name: String, pct: Double, color: Color) -> some View {
        HStack(spacing: 6) {
            if let logo {
                Image(nsImage: logo)
                    .renderingMode(.template)
                    .resizable()
                    .aspectRatio(contentMode: .fit)
                    .frame(height: 11)
                    .foregroundStyle(color)
            }
            Text(name)
                .font(.system(size: 11.5, weight: .bold, design: .rounded))
                .foregroundStyle(.white.opacity(0.78))
            Text("\(Int((pct * 100).rounded()))%")
                .font(.system(size: 11.5, weight: .heavy, design: .rounded))
                .foregroundStyle(color)
        }
    }

    private var weekBars: some View {
        VStack(alignment: .leading, spacing: 8) {
            let peak = max(data.dailyTokens.max() ?? 1, 1)
            HStack(alignment: .bottom, spacing: 10) {
                ForEach(Array(data.dailyTokens.enumerated()), id: \.offset) { i, tokens in
                    let isPeak = tokens == peak && tokens > 0
                    VStack(spacing: 6) {
                        RoundedRectangle(cornerRadius: 3, style: .continuous)
                            .fill(
                                isPeak
                                ? AnyShapeStyle(LinearGradient(
                                    colors: [IslandColor.claude, IslandColor.codex],
                                    startPoint: .top, endPoint: .bottom))
                                : AnyShapeStyle(Color.white.opacity(tokens > 0 ? 0.22 : 0.07))
                            )
                            .frame(height: max(5, 64 * CGFloat(tokens) / CGFloat(peak)))
                        Text(data.dayLetters.indices.contains(i) ? data.dayLetters[i] : "")
                            .font(.system(size: 9, weight: .bold, design: .rounded))
                            .foregroundStyle(.white.opacity(isPeak ? 0.75 : 0.32))
                    }
                    .frame(maxWidth: .infinity)
                }
            }
            .frame(height: 84, alignment: .bottom)
        }
    }

    private var modelRows: some View {
        VStack(alignment: .leading, spacing: 9) {
            ForEach(Array(data.topModels.enumerated()), id: \.element.id) { i, row in
                HStack(spacing: 10) {
                    Text("\(i + 1)")
                        .font(.system(size: 10, weight: .heavy, design: .monospaced))
                        .foregroundStyle(.white.opacity(0.35))
                        .frame(width: 10)
                    Text(row.name)
                        .font(.system(size: 12, weight: .semibold, design: .rounded))
                        .foregroundStyle(.white.opacity(0.8))
                        .lineLimit(1)
                    GeometryReader { geo in
                        ZStack(alignment: .leading) {
                            Capsule().fill(.white.opacity(0.07))
                            Capsule().fill(row.color.opacity(0.85))
                                .frame(width: max(3, geo.size.width * row.percent))
                        }
                    }
                    .frame(height: 4)
                    Text("\(Int((row.percent * 100).rounded()))%")
                        .font(.system(size: 11, weight: .heavy, design: .rounded))
                        .foregroundStyle(.white.opacity(0.6))
                        .frame(width: 34, alignment: .trailing)
                }
            }
        }
    }

    private var footer: some View {
        VStack(spacing: 14) {
            Rectangle()
                .fill(LinearGradient(colors: [.white.opacity(0.0), .white.opacity(0.14), .white.opacity(0.0)],
                                     startPoint: .leading, endPoint: .trailing))
                .frame(height: 1)
            // Brand + landing strip. The row doubles as the reserved sponsor
            // slot; the QR is the cross-platform landing hook — anyone who
            // sees the shared image (WeChat, Douyin, Android, anywhere)
            // scans straight into agent-island.dev.
            HStack(alignment: .center, spacing: 10) {
                if let icon = NSImage(named: NSImage.applicationIconName) {
                    Image(nsImage: icon)
                        .resizable()
                        .aspectRatio(contentMode: .fit)
                        .frame(width: 34, height: 34)
                }
                VStack(alignment: .leading, spacing: 3) {
                    Text("Agent Island")
                        .font(.system(size: 13, weight: .heavy, design: .rounded))
                        .foregroundStyle(.white.opacity(0.88))
                    Text("github.com/tristan666666/agent-island")
                        .font(.system(size: 8.5, weight: .semibold, design: .monospaced))
                        .foregroundStyle(.white.opacity(0.38))
                }
                Spacer()
                qrTile
            }
        }
    }

    /// White tile + crisp QR → agent-island.dev. The landing page that
    /// catches every share, on every platform, no share-API needed.
    private var qrTile: some View {
        ZStack {
            RoundedRectangle(cornerRadius: 7, style: .continuous)
                .fill(.white)
            if let qr = Self.landingQR {
                Image(nsImage: qr)
                    .interpolation(.none)
                    .resizable()
                    .frame(width: 40, height: 40)
            }
        }
        .frame(width: 50, height: 50)
    }

    private static let landingQR: NSImage? = makeQR("https://agent-island.dev")

    private static func makeQR(_ text: String) -> NSImage? {
        guard let data = text.data(using: .utf8),
              let filter = CIFilter(name: "CIQRCodeGenerator") else { return nil }
        filter.setValue(data, forKey: "inputMessage")
        filter.setValue("M", forKey: "inputCorrectionLevel")
        guard let output = filter.outputImage else { return nil }
        let scaled = output.transformed(by: CGAffineTransform(scaleX: 12, y: 12))
        let rep = NSCIImageRep(ciImage: scaled)
        let image = NSImage(size: rep.size)
        image.addRepresentation(rep)
        return image
    }

    // MARK: - Formatting

    /// (value, unit). Chinese counts in 亿/万 — the way the number is
    /// actually said — English in B/M/K.
    static func compactParts(_ n: Int, zh: Bool) -> (String, String) {
        let v = Double(n)
        if zh {
            if v >= 100_000_000 { return (trim(v / 100_000_000), "亿") }
            if v >= 10_000 { return (trim(v / 10_000), "万") }
            return ("\(n)", "")
        }
        switch v {
        case 1_000_000_000...: return (trim(v / 1_000_000_000), "B")
        case 1_000_000...:     return (trim(v / 1_000_000), "M")
        case 1_000...:         return (trim(v / 1_000), "K")
        default:               return ("\(n)", "")
        }
    }

    private static func trim(_ v: Double) -> String {
        // No trailing zeros — "99.5亿", never "99.50亿".
        var s = v >= 100 ? String(format: "%.0f", v) : String(format: "%.2f", v)
        if s.contains(".") {
            while s.hasSuffix("0") { s.removeLast() }
            if s.hasSuffix(".") { s.removeLast() }
        }
        return s
    }

    static func money(_ v: Double) -> String {
        let f = NumberFormatter()
        f.numberStyle = .decimal
        f.maximumFractionDigits = 0
        return f.string(from: NSNumber(value: v)) ?? String(format: "%.0f", v)
    }
}

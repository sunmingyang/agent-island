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
        let tokens: Int       // wire tokens this week
        let dollars: Double   // API value this week
        let percent: Double   // 0...1 of the combined week's dollars
        let isClaude: Bool
        let color: Color
        var isOthers: Bool = false
    }

    let rangeText: String
    let totalTokens: Int
    let totalDollars: Double
    let claudeShare: Double   // 0...1 of weekly tokens
    let dailyTokens: [Int]    // oldest → today, exactly 7
    let dayLetters: [String]
    let topModels: [ModelShare]
    /// "🏆 百亿俱乐部 · 累计 227 亿 Token" — the in-card milestone caption;
    /// nil until the first tier (100M lifetime) is crossed.
    let milestoneText: String?

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
        // Wire tokens (cache included) — same accounting as the hero total,
        // so the four rows visibly sum toward the headline number.
        let claudeRows = cost.claude.weekByModel.map {
            ModelShare(name: $0.displayName, tokens: $0.wireTokens, dollars: $0.dollars,
                       percent: $0.dollars / dollarUniverse, isClaude: true,
                       color: IslandColor.claude)
        }
        let codexRows = cost.codex.weekByModel.map {
            ModelShare(name: $0.displayName, tokens: $0.wireTokens, dollars: $0.dollars,
                       percent: $0.dollars / dollarUniverse, isClaude: false,
                       color: IslandColor.codex)
        }
        let all = (claudeRows + codexRows).sorted { $0.percent > $1.percent }
        // Top 5 by spend (fewer if the week only touched fewer); everything
        // past the fold folds into one dim "Others" row, so a 12-model week
        // renders exactly like a 5-model week. ≥0.5% keeps noise rows off.
        // Colors are a RANKED categorical palette — provider-shaded hues
        // made neighboring segments indistinguishable (owner, 2026-07-14);
        // the provider still reads from the model name itself.
        let palette: [Color] = [
            Color(red: 90/255, green: 168/255, blue: 240/255),   // blue
            Color(red: 204/255, green: 120/255, blue: 92/255),   // coral
            Color(red: 232/255, green: 194/255, blue: 104/255),  // amber
            Color(red: 91/255, green: 200/255, blue: 175/255),   // teal
            Color(red: 167/255, green: 139/255, blue: 250/255),  // violet
        ]
        var models = Array(all.filter { $0.percent >= 0.005 }.prefix(5))
            .enumerated().map { i, m in
                ModelShare(name: m.name, tokens: m.tokens, dollars: m.dollars,
                           percent: m.percent, isClaude: m.isClaude,
                           color: palette[min(i, palette.count - 1)])
            }
        let shownNames = Set(models.map(\.name))
        let rest = all.filter { !shownNames.contains($0.name) }
        if !rest.isEmpty {
            let restPercent = rest.reduce(0.0) { $0 + $1.percent }
            if restPercent >= 0.005 {
                models.append(ModelShare(
                    name: L10n.tr("Others"),
                    tokens: rest.reduce(0) { $0 + $1.tokens },
                    dollars: rest.reduce(0.0) { $0 + $1.dollars },
                    percent: restPercent,
                    isClaude: false,
                    color: Color(white: 0.42),
                    isOthers: true
                ))
            }
        }

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

        // Lifetime milestone caption — recognition rides the card itself.
        let lifetime = (cost.claude.dailyTokens + cost.codex.dailyTokens)
            .reduce(0) { $0 + $1.tokens }
        let milestoneText = MilestoneLadder.tokenTier(lifetime: lifetime).map { tier in
            "🏆 " + L10n.tr(tier.titleKey) + " · "
                + L10n.tr("lifetime %@ tokens", WeeklyReportCard.compactString(lifetime, zh: zh))
        }

        return WeeklyReportData(
            rangeText: range,
            totalTokens: total,
            totalDollars: dollars,
            claudeShare: total > 0 ? Double(claudeWeek) / Double(total) : 0,
            dailyTokens: daily,
            dayLetters: letters,
            topModels: models,
            milestoneText: milestoneText
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
                Spacer(minLength: 14)
                hero
                Spacer(minLength: 16)
                providerSplit
                Spacer(minLength: 18)
                weekBars
                Spacer(minLength: 18)
                modelRows
                Spacer(minLength: 16)
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
        // Title ABOVE the number, money line below — a bare "100亿" with no
        // label read as a number from nowhere.
        return VStack(alignment: .leading, spacing: 8) {
            Text(L10n.tr("tokens this week"))
                .font(.system(size: 14, weight: .bold, design: .rounded))
                .tracking(0.4)
                .foregroundStyle(.white.opacity(0.55))
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
            if data.totalDollars >= 1 {
                Text(L10n.tr("≈ $%@ API value", Self.money(data.totalDollars)))
                    .font(.system(size: 13.5, weight: .heavy, design: .rounded))
                    .foregroundStyle(Color(red: 0.55, green: 0.85, blue: 0.62))
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

    /// Donut + legend — every model carries all three numbers (tokens,
    /// dollars, share); the bare percent bars said too little.
    private var modelRows: some View {
        let zh = L10n.locale.identifier.hasPrefix("zh")
        return HStack(spacing: 20) {
            ZStack {
                Circle()
                    .stroke(.white.opacity(0.06), lineWidth: 13)
                ForEach(donutSegments, id: \.0.id) { row, from, to in
                    Circle()
                        .trim(from: CGFloat(from), to: CGFloat(to))
                        .stroke(row.color, style: StrokeStyle(lineWidth: 13, lineCap: .butt))
                }
            }
            // Segment 0 starts at 12 o'clock; the label rides outside the
            // rotation so it stays upright.
            .rotationEffect(.degrees(-90))
            .overlay {
                Text("TOP \(data.topModels.filter { !$0.isOthers }.count)")
                    .font(.system(size: 10.5, weight: .heavy, design: .rounded))
                    .tracking(0.8)
                    .foregroundStyle(.white.opacity(0.5))
            }
            .frame(width: 90, height: 90)

            VStack(alignment: .leading, spacing: 8) {
                ForEach(data.topModels) { row in
                    HStack(spacing: 7) {
                        Circle()
                            .fill(row.color)
                            .frame(width: 7, height: 7)
                        Text(row.name)
                            .font(.system(size: 11, weight: .bold, design: .rounded))
                            .foregroundStyle(.white.opacity(row.isOthers ? 0.5 : 0.85))
                            .lineLimit(1)
                        Spacer(minLength: 6)
                        Text(Self.compactString(row.tokens, zh: zh))
                            .font(.system(size: 10, weight: .heavy, design: .rounded))
                            .foregroundStyle(.white.opacity(0.5))
                        Text("$\(Self.money(row.dollars))")
                            .font(.system(size: 10, weight: .heavy, design: .rounded))
                            .foregroundStyle(Color(red: 0.55, green: 0.85, blue: 0.62).opacity(0.9))
                        Text("\(Int((row.percent * 100).rounded()))%")
                            .font(.system(size: 10, weight: .heavy, design: .rounded))
                            .foregroundStyle(.white.opacity(0.88))
                            .frame(width: 28, alignment: .trailing)
                    }
                }
            }
        }
    }

    /// Cumulative (row, from, to) sweep per model, with a hairline gap
    /// between segments so same-hue neighbors stay separable.
    private var donutSegments: [(WeeklyReportData.ModelShare, Double, Double)] {
        var cum = 0.0
        return data.topModels.map { row in
            let start = cum
            cum += row.percent
            let gap = row.percent > 0.03 ? 0.006 : 0.0
            return (row, start + gap, max(start + gap, cum - gap))
        }
    }

    private var footer: some View {
        VStack(spacing: 12) {
            // The milestone caption — the "你已经很牛逼了" line, in the card.
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

    static func compactString(_ n: Int, zh: Bool) -> String {
        let parts = compactParts(n, zh: zh)
        return parts.0 + parts.1
    }

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

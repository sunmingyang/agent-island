import AppKit

let teal = NSColor(red: 0x20/255.0, green: 0xC0/255.0, blue: 0xB0/255.0, alpha: 1)
let orange = NSColor(red: 0xDF/255.0, green: 0x8A/255.0, blue: 0x50/255.0, alpha: 1)
let gold = NSColor(red: 0xE3/255.0, green: 0xB3/255.0, blue: 0x4F/255.0, alpha: 1)

func ray(_ ctx: NSGraphicsContext, center: CGPoint, angle: CGFloat, r0: CGFloat, r1: CGFloat, width: CGFloat, color: NSColor) {
    let p = NSBezierPath()
    p.lineWidth = width
    p.lineCapStyle = .round
    let a = angle * .pi / 180
    p.move(to: CGPoint(x: center.x + cos(a) * r0, y: center.y + sin(a) * r0))
    p.line(to: CGPoint(x: center.x + cos(a) * r1, y: center.y + sin(a) * r1))
    color.setStroke()
    p.stroke()
}

// Draws the Agent Island mark: 6 teal capsules, 6 orange ROUND-capped rays
// (was: sharp needles — liquid-glass/icon-composer can't handle the acute
// points), gold core.
func drawMark(center: CGPoint, scale: CGFloat) {
    let ctx = NSGraphicsContext.current!
    for a: CGFloat in [0, 60, 120, 180, 240, 300] {
        ray(ctx, center: center, angle: a, r0: 132 * scale, r1: 286 * scale, width: 84 * scale, color: teal)
    }
    for a: CGFloat in [90, 270] {
        ray(ctx, center: center, angle: a, r0: 176 * scale, r1: 402 * scale, width: 34 * scale, color: orange)
    }
    for a: CGFloat in [30, 150, 210, 330] {
        ray(ctx, center: center, angle: a, r0: 176 * scale, r1: 372 * scale, width: 34 * scale, color: orange)
    }
    let cr = 46 * scale
    gold.setFill()
    NSBezierPath(ovalIn: CGRect(x: center.x - cr, y: center.y - cr, width: cr * 2, height: cr * 2)).fill()
}

func savePNG(_ image: NSImage, to path: String, pixelsWide: Int, pixelsHigh: Int) {
    let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: pixelsWide, pixelsHigh: pixelsHigh,
                               bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false,
                               colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0)!
    rep.size = image.size
    NSGraphicsContext.saveGraphicsState()
    NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: rep)
    image.draw(in: CGRect(origin: .zero, size: image.size))
    NSGraphicsContext.restoreGraphicsState()
    try! rep.representation(using: .png, properties: [:])!.write(to: URL(fileURLWithPath: path))
}

// ---------- 1) App icon (1024 master) ----------
func drawIcon(size: CGFloat) -> NSImage {
    let img = NSImage(size: CGSize(width: size, height: size))
    img.lockFocus()
    let s = size / 1024.0
    // Big-Sur squircle: 824×824 centered, radius 185.
    let rect = CGRect(x: 100 * s, y: 100 * s, width: 824 * s, height: 824 * s)
    let squircle = NSBezierPath(roundedRect: rect, xRadius: 185 * s, yRadius: 185 * s)
    // Not pure black — keep a breath of luminance (design review).
    let bg = NSGradient(colors: [
        NSColor(red: 0x19/255.0, green: 0x1D/255.0, blue: 0x25/255.0, alpha: 1),
        NSColor(red: 0x0B/255.0, green: 0x0D/255.0, blue: 0x12/255.0, alpha: 1),
    ])!
    bg.draw(in: squircle, angle: -90)
    // Soft top sheen.
    NSGraphicsContext.current?.saveGraphicsState()
    squircle.addClip()
    let sheen = NSGradient(colors: [
        NSColor(white: 1, alpha: 0.07),
        NSColor(white: 1, alpha: 0.0),
    ])!
    sheen.draw(in: CGRect(x: 100 * s, y: 624 * s, width: 824 * s, height: 300 * s), angle: -90)
    NSGraphicsContext.current?.restoreGraphicsState()
    drawMark(center: CGPoint(x: size / 2, y: size / 2), scale: s * 0.88)
    img.unlockFocus()
    return img
}

let iconsetDir = CommandLine.arguments[1]
let sizes: [(Int, String)] = [(16, "16x16"), (32, "16x16@2x"), (32, "32x32"), (64, "32x32@2x"),
                              (128, "128x128"), (256, "128x128@2x"), (256, "256x256"), (512, "256x256@2x"),
                              (512, "512x512"), (1024, "512x512@2x")]
for (px, name) in sizes {
    let img = drawIcon(size: CGFloat(px))
    savePNG(img, to: "\(iconsetDir)/icon_\(name).png", pixelsWide: px, pixelsHigh: px)
}

// ---------- 2) DMG background (660×420 @1x/@2x) ----------
func drawBackground(scale: CGFloat) -> NSImage {
    let w: CGFloat = 660, h: CGFloat = 420
    let img = NSImage(size: CGSize(width: w, height: h))
    img.lockFocus()
    NSGradient(colors: [
        NSColor(red: 0x14/255.0, green: 0x17/255.0, blue: 0x1E/255.0, alpha: 1),
        NSColor(red: 0x0A/255.0, green: 0x0C/255.0, blue: 0x10/255.0, alpha: 1),
    ])!.draw(in: CGRect(x: 0, y: 0, width: w, height: h), angle: -90)
    // Faint teal aura behind the wordmark.
    NSGradient(colors: [teal.withAlphaComponent(0.10), teal.withAlphaComponent(0.0)])!
        .draw(fromCenter: CGPoint(x: w / 2, y: h - 40), radius: 0,
              toCenter: CGPoint(x: w / 2, y: h - 40), radius: 260, options: [])
    // Small mark + wordmark, top center.
    drawMark(center: CGPoint(x: w / 2 - 92, y: h - 58), scale: 0.038)
    let title = NSAttributedString(string: "Agent Island", attributes: [
        .font: NSFont.systemFont(ofSize: 22, weight: .semibold),
        .foregroundColor: NSColor(white: 1, alpha: 0.90),
    ])
    title.draw(at: CGPoint(x: w / 2 - 60, y: h - 70))
    // Arrow between the two icon slots (centers x=165 / x=500, y_top=200 → y_bottom=220).
    let arrow = NSBezierPath()
    arrow.lineWidth = 4
    arrow.lineCapStyle = .round
    arrow.lineJoinStyle = .round
    arrow.move(to: CGPoint(x: 262, y: 220))
    arrow.line(to: CGPoint(x: 398, y: 220))
    arrow.move(to: CGPoint(x: 372, y: 246))
    arrow.line(to: CGPoint(x: 398, y: 220))
    arrow.line(to: CGPoint(x: 372, y: 194))
    NSColor(white: 1, alpha: 0.30).setStroke()
    arrow.stroke()
    img.unlockFocus()
    return img
}

let bgDir = CommandLine.arguments[2]
savePNG(drawBackground(scale: 1), to: "\(bgDir)/background.png", pixelsWide: 660, pixelsHigh: 420)
savePNG(drawBackground(scale: 2), to: "\(bgDir)/background@2x.png", pixelsWide: 1320, pixelsHigh: 840)
print("assets generated")

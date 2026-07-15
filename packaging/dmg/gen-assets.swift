import AppKit

let teal = NSColor(red: 0x20/255.0, green: 0xC0/255.0, blue: 0xB0/255.0, alpha: 1)
let orange = NSColor(red: 0xDF/255.0, green: 0x8A/255.0, blue: 0x50/255.0, alpha: 1)
let gold = NSColor(red: 0xE3/255.0, green: 0xB3/255.0, blue: 0x4F/255.0, alpha: 1)

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
    // Real logo asset + wordmark, top center — the mark is the brand,
    // never a redrawn approximation.
    if let logo = NSImage(contentsOfFile: "Assets/agent-island-logo.png") {
        logo.draw(in: CGRect(x: w / 2 - 106, y: h - 76, width: 36, height: 36),
                  from: .zero, operation: .sourceOver, fraction: 1.0)
    }
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

let bgDir = CommandLine.arguments[1]
savePNG(drawBackground(scale: 1), to: "\(bgDir)/background.png", pixelsWide: 660, pixelsHigh: 420)
savePNG(drawBackground(scale: 2), to: "\(bgDir)/background@2x.png", pixelsWide: 1320, pixelsHigh: 840)
print("assets generated")

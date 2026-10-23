// Renders the app icon (a near-black squircle with a quiet check ring) into an .iconset.
// Usage: make_icon <output.iconset>
import AppKit

let output = URL(fileURLWithPath: CommandLine.arguments.dropFirst().first ?? "AppIcon.iconset")
try FileManager.default.createDirectory(at: output, withIntermediateDirectories: true)

func render(pixels: Int) -> Data {
    let rep = NSBitmapImageRep(
        bitmapDataPlanes: nil, pixelsWide: pixels, pixelsHigh: pixels,
        bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false,
        colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0
    )!
    NSGraphicsContext.saveGraphicsState()
    NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: rep)
    let s = CGFloat(pixels)

    // Apple's icon grid: the body sits inside a ~10% margin.
    let inset = s * 0.098
    let body = NSRect(x: inset, y: inset, width: s - inset * 2, height: s - inset * 2)
    let radius = body.width * 0.225
    let shape = NSBezierPath(roundedRect: body, xRadius: radius, yRadius: radius)

    NSGraphicsContext.current?.saveGraphicsState()
    let shadow = NSShadow()
    shadow.shadowColor = NSColor.black.withAlphaComponent(0.45)
    shadow.shadowBlurRadius = s * 0.025
    shadow.shadowOffset = NSSize(width: 0, height: -s * 0.01)
    shadow.set()
    NSColor(srgbRed: 0.063, green: 0.067, blue: 0.078, alpha: 1).setFill()
    shape.fill()
    NSGraphicsContext.current?.restoreGraphicsState()

    NSGradient(colors: [
        NSColor(srgbRed: 0.095, green: 0.1, blue: 0.115, alpha: 1),
        NSColor(srgbRed: 0.031, green: 0.035, blue: 0.043, alpha: 1)
    ])!.draw(in: shape, angle: -90)

    NSColor.white.withAlphaComponent(0.08).setStroke()
    shape.lineWidth = max(1, s * 0.004)
    shape.stroke()

    // Check ring.
    let ringDiameter = body.width * 0.46
    let ringRect = NSRect(x: body.midX - ringDiameter / 2, y: body.midY - ringDiameter / 2, width: ringDiameter, height: ringDiameter)
    let ring = NSBezierPath(ovalIn: ringRect)
    ring.lineWidth = body.width * 0.034
    NSColor(srgbRed: 0.204, green: 0.78, blue: 0.482, alpha: 1).setStroke()
    ring.stroke()

    // Tick.
    let tick = NSBezierPath()
    tick.move(to: NSPoint(x: ringRect.minX + ringDiameter * 0.28, y: ringRect.minY + ringDiameter * 0.5))
    tick.line(to: NSPoint(x: ringRect.minX + ringDiameter * 0.44, y: ringRect.minY + ringDiameter * 0.34))
    tick.line(to: NSPoint(x: ringRect.minX + ringDiameter * 0.73, y: ringRect.minY + ringDiameter * 0.66))
    tick.lineWidth = body.width * 0.04
    tick.lineCapStyle = .round
    tick.lineJoinStyle = .round
    NSColor.white.withAlphaComponent(0.94).setStroke()
    tick.stroke()

    NSGraphicsContext.restoreGraphicsState()
    return rep.representation(using: .png, properties: [:])!
}

for points in [16, 32, 128, 256, 512] {
    for scale in [1, 2] {
        let name = scale == 1 ? "icon_\(points)x\(points).png" : "icon_\(points)x\(points)@2x.png"
        try render(pixels: points * scale).write(to: output.appendingPathComponent(name))
    }
}
print("Wrote \(output.path)")

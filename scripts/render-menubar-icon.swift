// Превращает Support/Icons/MenuBarIcon.svg в PNG 18 и 36 пикселей (1x и 2x).
// SVG напрямую старые macOS могут прочитать «пустым», а PNG работает везде.
// Запуск из корня репозитория: swift scripts/render-menubar-icon.swift
import AppKit

let svg = NSImage(contentsOfFile: "Support/Icons/MenuBarIcon.svg")!
for (scale, name) in [(1, "MenuBarIcon.png"), (2, "MenuBarIcon@2x.png")] {
    let px = 18 * scale
    let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: px, pixelsHigh: px, bitsPerSample: 8,
                               samplesPerPixel: 4, hasAlpha: true, isPlanar: false,
                               colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0)!
    NSGraphicsContext.saveGraphicsState()
    NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: rep)
    svg.draw(in: NSRect(x: 0, y: 0, width: px, height: px))
    NSGraphicsContext.restoreGraphicsState()
    try! rep.representation(using: .png, properties: [:])!.write(to: URL(fileURLWithPath: "Support/Icons/\(name)"))
    print("Support/Icons/\(name): \(px)×\(px)")
}

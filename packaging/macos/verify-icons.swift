import AppKit

// Exercise the same bundle icon lookup Finder uses, not just file existence.
let bundleURL = URL(fileURLWithPath: CommandLine.arguments[1])
let outputURL = URL(fileURLWithPath: CommandLine.arguments[2])
func require(_ condition: Bool, _ message: String) {
    if !condition { fputs(message + "\n", stderr); exit(1) }
}
let bundle = Bundle(url: bundleURL)!
let iconName = bundle.object(forInfoDictionaryKey: "CFBundleIconFile") as? String
require(iconName == "SlickWatch.icns", "Missing CFBundleIconFile")
let iconURL = bundleURL.appendingPathComponent("Contents/Resources/SlickWatch.icns")
let nativeIcon = NSImage(contentsOf: iconURL)
require(nativeIcon != nil && nativeIcon!.isValid, "AppKit cannot decode the ICNS file")
let finderIcon = NSWorkspace.shared.icon(forFile: bundleURL.path)
let pixels = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: 128, pixelsHigh: 128,
    bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false,
    colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0)!
NSGraphicsContext.saveGraphicsState()
NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: pixels)
finderIcon.draw(in: NSRect(x: 0, y: 0, width: 128, height: 128))
NSGraphicsContext.restoreGraphicsState()
var radarPixels = 0
for y in 0..<128 {
    for x in 0..<128 {
        let color = pixels.colorAt(x: x, y: y)!.usingColorSpace(.deviceRGB)!
        if color.alphaComponent > 0.5 && color.greenComponent > color.redComponent * 1.3 && color.greenComponent >= color.blueComponent * 0.9 {
            radarPixels += 1
        }
    }
}
try pixels.representation(using: .png, properties: [:])!.write(to: outputURL)
require(radarPixels > 128 * 128 / 5, "Finder resolved a generic/blank icon instead of the teal radar (\(radarPixels) teal pixels)")
print("PASS: AppKit decoded ICNS and Finder resolved the radar icon (\(radarPixels) teal pixels)")

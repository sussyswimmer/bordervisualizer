import AppKit

/// The menu bar icon: a rounded screen outline with a softer line inside it, the glow (doc 06 §2's icon). A template
/// image, so macOS tints it for the menu bar's light or dark appearance.
enum StatusIcon {
    static func make() -> NSImage {
        let image = NSImage(size: NSSize(width: 18, height: 18), flipped: false) { rect in
            let screen = rect.insetBy(dx: 2, dy: 3.5)
            let outline = NSBezierPath(roundedRect: screen, xRadius: 3, yRadius: 3)
            outline.lineWidth = 1.5
            NSColor.black.setStroke()
            outline.stroke()

            let glow = NSBezierPath(roundedRect: screen.insetBy(dx: 2.5, dy: 2.5), xRadius: 1.5, yRadius: 1.5)
            glow.lineWidth = 1
            NSColor.black.withAlphaComponent(0.4).setStroke()
            glow.stroke()
            return true
        }
        image.isTemplate = true
        return image
    }
}

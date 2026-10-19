import AppKit
import QuartzCore

/// One screen's overlay window (docs/10-MACOS.md §3.1, Apple DTS's setup for a window over every Space, full-screen
/// ones included): borderless, non-activating, click-through, clear, above everything, never key or main.
final class OverlayPanel: NSPanel {
    let metalLayer = CAMetalLayer()

    convenience init(frame: NSRect, scale: CGFloat) {
        self.init(contentRect: frame, styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        level = .screenSaver
        collectionBehavior = [.canJoinAllSpaces, .canJoinAllApplications, .fullScreenAuxiliary, .stationary,
                              .ignoresCycle]
        ignoresMouseEvents = true
        isOpaque = false
        backgroundColor = .clear
        hasShadow = false
        hidesOnDeactivate = false
        isReleasedWhenClosed = false
        animationBehavior = .none
        isExcludedFromWindowsMenu = true

        let view = NSView(frame: NSRect(origin: .zero, size: frame.size))
        metalLayer.contentsScale = scale
        metalLayer.frame = view.bounds
        metalLayer.drawableSize = CGSize(width: (frame.width * scale).rounded(), height: (frame.height * scale).rounded())
        view.layer = metalLayer
        view.wantsLayer = true
        contentView = view
        setFrame(frame, display: false)
    }

    override var canBecomeKey: Bool { false }
    override var canBecomeMain: Bool { false }
}

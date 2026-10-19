import AppKit
import CRimlight
import Metal
import QuartzCore
import SwiftUI

/// The Appearance page's live preview (doc 06 §3.1): a 16:9 card showing the glow with the current settings, colours
/// and music, drawn by the overlay's own shader (Core's MacPreview supplies the constants) about 30 times a second
/// while the window is on screen. Windows draws the same with a CPU port (GlowRaster); here it can't drift.
struct GlowPreviewView: NSViewRepresentable {
    let model: SettingsModel

    func makeNSView(context: Context) -> PreviewNSView {
        PreviewNSView(model: model)
    }

    func updateNSView(_ view: PreviewNSView, context: Context) {}
}

final class PreviewNSView: NSView {
    private let model: SettingsModel
    private let metal: MetalGlow?
    private let preview: RlPreview
    private let metalLayer = CAMetalLayer()
    private let inFlight = InFlightCounter()
    private var timer: Timer?
    private var occlusion: NSObjectProtocol?

    init(model: SettingsModel) {
        self.model = model
        metal = try? MetalGlow(device: model.device)
        preview = rl_preview_create(model.loop.handle)
        super.init(frame: .zero)
        metal?.configure(metalLayer)
        metalLayer.backgroundColor = CGColor(gray: 0.07, alpha: 1) // the "screen" behind the glow
        metalLayer.cornerRadius = 10
        metalLayer.masksToBounds = true
        layer = metalLayer
        wantsLayer = true
        setAccessibilityElement(true)
        setAccessibilityRole(.image)
        setAccessibilityLabel("Preview of the glow")
    }

    @available(*, unavailable)
    required init?(coder: NSCoder) { fatalError("init(coder:) is not used") }

    // The timer stops when the view leaves its window (viewDidMoveToWindow), before it can be freed.
    deinit {
        rl_preview_destroy(preview)
    }

    override func viewDidMoveToWindow() {
        super.viewDidMoveToWindow()
        if let occlusion { NotificationCenter.default.removeObserver(occlusion) }
        occlusion = nil
        guard let window else { return stop() }
        occlusion = NotificationCenter.default.addObserver(
            forName: NSWindow.didChangeOcclusionStateNotification, object: window, queue: .main
        ) { [weak self] _ in
            MainActor.assumeIsolated { self?.followVisibility() }
        }
        followVisibility()
    }

    override func layout() {
        super.layout()
        let scale = window?.backingScaleFactor ?? 2
        metalLayer.contentsScale = scale
        metalLayer.drawableSize = CGSize(width: max(bounds.width * scale, 2), height: max(bounds.height * scale, 2))
    }

    /// Draws only while the window is on screen: closed, minimized or covered, the preview costs nothing.
    private func followVisibility() {
        if window?.occlusionState.contains(.visible) == true { start() } else { stop() }
    }

    private func start() {
        guard timer == nil, metal != nil, preview != 0 else { return }
        model.startMeter() // the preview follows the music too
        let timer = Timer(timeInterval: 1.0 / 30, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated { self?.tick() }
        }
        RunLoop.main.add(timer, forMode: .common) // keeps drawing while a slider is dragged
        self.timer = timer
        tick()
    }

    private func stop() {
        guard let timer else { return }
        timer.invalidate()
        self.timer = nil
        model.stopMeter()
    }

    private func tick() {
        guard let metal else { return }
        let size = metalLayer.drawableSize
        var constants = RlGlowConstants()
        let result = rl_preview_frame(preview, CACurrentMediaTime(), Int32(size.width), Int32(size.height), &constants)
        if result < 0 { return stop() }
        if result == 1, let texels = rl_preview_gradient(preview) { metal.upload(gradient: texels) }
        _ = metal.draw(constants, on: metalLayer, inFlight: inFlight, now: CACurrentMediaTime())
    }
}

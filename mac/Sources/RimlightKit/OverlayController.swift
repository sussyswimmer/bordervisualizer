import AppKit
import CRimlight
import QuartzCore

/// Makes one overlay per chosen screen and keeps them in step with the displays and the settings (main thread). The
/// render thread draws them.
@MainActor
final class OverlayController {
    private let loop: CoreLoop
    private let metal: MetalGlow
    private let render: RenderThread
    private var panels: [OverlayPanel] = []
    private var layoutKey = ""
    private var rebuildPending = false
    private var observer: NSObjectProtocol?

    init(loop: CoreLoop, metal: MetalGlow) {
        self.loop = loop
        self.metal = metal
        render = RenderThread(loop: loop, metal: metal)
        observer = NotificationCenter.default.addObserver(
            forName: NSApplication.didChangeScreenParametersNotification, object: nil, queue: .main
        ) { [weak self] _ in
            MainActor.assumeIsolated { self?.scheduleRebuild() }
        }
        rebuild()
    }

    /// The settings or demo mode changed: the render thread takes them on its next inputs, and the overlays are
    /// rebuilt if the screens they cover changed.
    func settingsChanged() {
        render.inputsChanged()
        rebuild()
    }

    /// Displays changed: rebuild after they settle (300 ms, doc 02).
    private func scheduleRebuild() {
        guard !rebuildPending else { return }
        rebuildPending = true
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.3) { [weak self] in
            MainActor.assumeIsolated {
                self?.rebuildPending = false
                self?.rebuild(force: true)
            }
        }
    }

    private func rebuild(force: Bool = false) {
        guard let settings = loop.settings else { return }
        let screens = Self.chosenScreens(settings)
        let frames = screens.map { settings.coverTaskbar ? $0.frame : $0.visibleFrame }
        let key = zip(screens, frames).map { "\(Self.uuid(of: $0.0) ?? "?")@\($0.1)x\($0.0.backingScaleFactor)" }
            .joined(separator: ";")
        if !force && key == layoutKey { return }
        layoutKey = key

        var surfaces: [OverlaySurface] = []
        var newPanels: [OverlayPanel] = []
        for (screen, frame) in zip(screens, frames) {
            let scale = screen.backingScaleFactor
            let panel = OverlayPanel(frame: frame, scale: scale)
            metal.configure(panel.metalLayer)
            let size = panel.metalLayer.drawableSize
            let info = RlSurface(widthPx: Int32(size.width), heightPx: Int32(size.height),
                                 pixelsPerPoint: Float(scale), paused: 0)
            surfaces.append(OverlaySurface(panel: panel, layer: panel.metalLayer, info: info))
            newPanels.append(panel)
        }
        panels = newPanels

        // Pacing follows the primary screen (the one with the menu bar), as Windows follows the primary monitor.
        let primary = NSScreen.screens.first
        let link = primary?.displayLink(target: render, selector: RenderThread.tickSelector)
        let refresh = Double(primary?.maximumFramesPerSecond ?? 60)
        render.setSurfaces(surfaces, link: link, refreshHz: refresh, onBattery: false)
    }

    /// The screens the settings choose (doc 06 §1 Monitors): all, the primary, or those whose display UUID is listed.
    static func chosenScreens(_ settings: AppSettings) -> [NSScreen] {
        let all = NSScreen.screens
        switch settings.monitors {
        case "PrimaryOnly":
            return Array(all.prefix(1))
        case "Custom":
            let ids = Set(settings.customMonitorIds.map { $0.uppercased() })
            return all.filter { uuid(of: $0).map { ids.contains($0.uppercased()) } ?? false }
        default:
            return all
        }
    }

    /// A display's stable UUID (CGDisplayCreateUUIDFromDisplayID): the same across reboots and replugging.
    static func uuid(of screen: NSScreen) -> String? {
        guard let number = screen.deviceDescription[NSDeviceDescriptionKey("NSScreenNumber")] as? NSNumber,
              let uuid = CGDisplayCreateUUIDFromDisplayID(number.uint32Value)?.takeRetainedValue() else { return nil }
        return CFUUIDCreateString(nil, uuid) as String
    }
}

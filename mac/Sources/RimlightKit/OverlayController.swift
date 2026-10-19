import AppKit
import CRimlight
import QuartzCore

/// Makes one overlay per chosen screen and keeps them in step with the displays, the settings and the power state
/// (main thread). The render thread draws them.
@MainActor
final class OverlayController {
    private let loop: CoreLoop
    private let metal: MetalGlow
    private let render: RenderThread
    private var panels: [OverlayPanel] = []
    private var screenIDs: [String] = []       // the overlays' displays, in surface order
    private var layoutKey = ""
    private var rebuildPending = false
    private var observer: NSObjectProtocol?
    private var onBattery = false              // on battery or in Low Power Mode (M5)
    private var fullscreen: Set<String> = []   // displays a full-screen window covers (M5)

    init(loop: CoreLoop, metal: MetalGlow, tap: SystemAudioTap?) {
        self.loop = loop
        self.metal = metal
        render = RenderThread(loop: loop, metal: metal, tap: tap)
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

    /// On battery (or Low Power Mode, which counts the same, 10-MACOS §5): FramePacer caps at 30 fps and "Reduce"
    /// halves the render scale, "Pause" pauses the glow. A change rebuilds the surfaces at the new scale.
    func setOnBattery(_ value: Bool) {
        guard value != onBattery else { return }
        onBattery = value
        rebuild(force: true)
    }

    /// Displays (by UUID) that a full-screen window covers: the glow fades out there (doc 04 §4) and keeps its window.
    func setFullscreen(_ displays: Set<String>) {
        guard displays != fullscreen else { return }
        fullscreen = displays
        let paused = screenIDs.map { displays.contains($0) }
        render.setSurfacesPaused(paused)
    }

    /// The displays' UUIDs in surface order, with their frames, for the full-screen check.
    var overlaidScreens: [(id: String, frame: NSRect)] {
        NSScreen.screens.compactMap { screen in
            guard let id = Self.uuid(of: screen), screenIDs.contains(id) else { return nil }
            return (id, screen.frame)
        }
    }

    /// Re-reads the layout (the Dock moved or auto-hid, which no notification reliably reports, 10-MACOS §3.1).
    func checkLayout() {
        rebuild()
    }

    /// After waking from sleep: displays and their link may have changed under us.
    func wake() {
        rebuild(force: true)
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
        // "Reduce on battery": half render scale (doc 04 §2). The layer stretches the smaller drawable back.
        let renderScale: CGFloat = onBattery && settings.onBattery == "Reduce" ? 0.5 : 1
        let key = zip(screens, frames).map { "\(Self.uuid(of: $0.0) ?? "?")@\($0.1)x\($0.0.backingScaleFactor)" }
            .joined(separator: ";") + (settings.hideFromScreenCapture ? ";hidden" : "") + ";scale\(renderScale)"
            + ";battery\(onBattery)"
        if !force && key == layoutKey { return }
        layoutKey = key

        var surfaces: [OverlaySurface] = []
        var newPanels: [OverlayPanel] = []
        var ids: [String] = []
        for (screen, frame) in zip(screens, frames) {
            let scale = screen.backingScaleFactor
            let panel = OverlayPanel(frame: frame, scale: scale)
            // "Hide from screen capture" (PRD §5, default off); some recorders may still show it (10-MACOS §3.1).
            panel.sharingType = settings.hideFromScreenCapture ? .none : .readOnly
            metal.configure(panel.metalLayer)
            let full = panel.metalLayer.drawableSize
            panel.metalLayer.drawableSize = CGSize(width: max((full.width * renderScale).rounded(), 2),
                                                   height: max((full.height * renderScale).rounded(), 2))
            let size = panel.metalLayer.drawableSize
            let id = Self.uuid(of: screen) ?? ""
            let info = RlSurface(widthPx: Int32(size.width), heightPx: Int32(size.height),
                                 pixelsPerPoint: Float(scale * renderScale), paused: fullscreen.contains(id) ? 1 : 0)
            surfaces.append(OverlaySurface(panel: panel, layer: panel.metalLayer, info: info))
            newPanels.append(panel)
            ids.append(id)
        }
        panels = newPanels
        screenIDs = ids

        // Pacing follows the primary screen (the one with the menu bar), as Windows follows the primary monitor.
        let primary = NSScreen.screens.first
        let link = primary?.displayLink(target: render, selector: RenderThread.tickSelector)
        let refresh = Double(primary?.maximumFramesPerSecond ?? 60)
        render.setSurfaces(surfaces, link: link, refreshHz: refresh, onBattery: onBattery)
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

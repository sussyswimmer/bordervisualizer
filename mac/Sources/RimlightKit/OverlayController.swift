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
    private var onBattery = false              // on battery (M5)
    private var lowPower = false               // Low Power Mode (M5)
    private var refreshHz: Double = 60
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

    /// On battery the OnBattery setting applies (Reduce: 30 fps at half scale; Pause); Low Power Mode always reduces
    /// (10-MACOS §5). Only a change of render scale makes new windows; otherwise the glow carries on unbroken.
    func setPower(onBattery: Bool, lowPower: Bool) {
        guard onBattery != self.onBattery || lowPower != self.lowPower else { return }
        let scaleBefore = renderScale
        self.onBattery = onBattery
        self.lowPower = lowPower
        if renderScale != scaleBefore {
            rebuild(force: true)
        } else {
            render.setDisplay(refreshHz: refreshHz, onBattery: onBattery, lowPower: lowPower)
        }
    }

    /// Half render scale (doc 04 §2) under "Reduce on battery" or Low Power Mode. The layer stretches the smaller
    /// drawable back.
    private var renderScale: CGFloat {
        let reduce = loop.settings?.onBattery == "Reduce"
        return (onBattery && reduce) || lowPower ? 0.5 : 1
    }

    /// Displays (by UUID) that a full-screen window covers: the glow fades out there (doc 04 §4) and keeps its window.
    func setFullscreen(_ displays: Set<String>) {
        guard displays != fullscreen else { return }
        fullscreen = displays
        let paused = screenIDs.map { displays.contains($0) }
        render.setSurfacesPaused(paused)
    }

    /// The overlaid displays, with their frames and the notch's height, for the full-screen check.
    var overlaidScreens: [SystemWatcher.Display] {
        NSScreen.screens.compactMap { screen in
            guard let id = Self.uuid(of: screen), screenIDs.contains(id) else { return nil }
            return SystemWatcher.Display(id: id, frame: screen.frame, topInset: screen.safeAreaInsets.top)
        }
    }

    /// Re-reads the layout (the Dock moved or auto-hid, which no notification reliably reports, 10-MACOS §3.1).
    func checkLayout() {
        guard !rebuildPending else { return } // displays are settling: the debounced rebuild follows
        rebuild()
    }

    /// After waking from sleep: displays and their link may have changed under us. Rebuilt once they settle.
    func wake() {
        scheduleRebuild()
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
        let renderScale = self.renderScale
        let key = zip(screens, frames).map { "\(Self.uuid(of: $0.0) ?? "?")@\($0.1)x\($0.0.backingScaleFactor)" }
            .joined(separator: ";") + (settings.hideFromScreenCapture ? ";hidden" : "") + ";scale\(renderScale)"
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
            // The light goes round the camera notch (Maxwell's request: "detect if theres a notch and go around it").
            let notch = Self.notch(screenFrame: screen.frame, panelFrame: frame, topInset: screen.safeAreaInsets.top,
                                   leftAreaWidth: screen.auxiliaryTopLeftArea?.width,
                                   rightAreaWidth: screen.auxiliaryTopRightArea?.width) ?? .zero
            let info = RlSurface(widthPx: Int32(size.width), heightPx: Int32(size.height),
                                 pixelsPerPoint: Float(scale * renderScale), paused: fullscreen.contains(id) ? 1 : 0,
                                 notchLeftPt: Float(notch.minX), notchRightPt: Float(notch.maxX),
                                 notchHeightPt: Float(notch.height), pad: 0)
            surfaces.append(OverlaySurface(panel: panel, layer: panel.metalLayer, info: info))
            newPanels.append(panel)
            ids.append(id)
        }
        panels = newPanels
        screenIDs = ids

        // Pacing follows the primary screen (the one with the menu bar), as Windows follows the primary monitor.
        let primary = NSScreen.screens.first
        let link = primary?.displayLink(target: render, selector: RenderThread.tickSelector)
        refreshHz = Double(primary?.maximumFramesPerSecond ?? 60)
        render.setSurfaces(surfaces, link: link, refreshHz: refreshHz, onBattery: onBattery, lowPower: lowPower)
    }

    /// The camera notch of a screen whose frame is `screenFrame`, as a rectangle in points from the top-left of an
    /// overlay at `panelFrame` (y down, as RlSurface takes it), or nil when the overlay has none. Both frames are
    /// AppKit's (y up). `topInset` is the screen's safe area at the top, the notch's height (0 on a screen without
    /// one); `leftAreaWidth` and `rightAreaWidth` are the widths of the menu bar's areas either side of it
    /// (NSScreen.auxiliaryTopLeftArea and auxiliaryTopRightArea), measured from the screen's own left and right edges.
    /// An overlay on the visible frame starts below the menu bar, which is at least as tall as the notch: no notch.
    nonisolated static func notch(screenFrame: CGRect, panelFrame: CGRect, topInset: CGFloat, leftAreaWidth: CGFloat?,
                                  rightAreaWidth: CGFloat?) -> CGRect? {
        guard topInset > 0, let leftAreaWidth, let rightAreaWidth else { return nil }
        let left = leftAreaWidth
        let right = screenFrame.width - rightAreaWidth
        guard right > left else { return nil }
        let dx = panelFrame.minX - screenFrame.minX     // the overlay's left edge, right of the screen's
        let topGap = screenFrame.maxY - panelFrame.maxY // the overlay's top edge, below the screen's
        let height = topInset - topGap
        guard height > 0 else { return nil }
        return CGRect(x: left - dx, y: 0, width: right - left, height: height)
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

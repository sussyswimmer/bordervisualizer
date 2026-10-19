import AppKit
import CRimlight
import QuartzCore
import os

/// One screen's overlay as the render thread sees it.
struct OverlaySurface {
    let panel: OverlayPanel
    let layer: CAMetalLayer
    let info: RlSurface
    let inFlight = InFlightCounter()
}

/// Drawables submitted to a screen and not yet shown. Each drawable's presented handler holds the counter, so it
/// outlives a surface that is replaced while frames are still on their way.
final class InFlightCounter {
    let counter: OpaquePointer

    init() {
        guard let counter = rlc_counter_create() else { fatalError("Out of memory") }
        self.counter = counter
    }

    deinit {
        rlc_counter_destroy(counter)
    }

    var value: Int32 { rlc_counter_load(counter) }

    func add(_ delta: Int32) {
        rlc_counter_add(counter, delta)
    }
}

/// The glow's render thread (doc 02, docs/10-MACOS.md §3.4): owns Metal and the loop's frame calls, and waits as
/// FramePacer says: on the main screen's display link at the full rate, on a timer at the slow rate, and not at all
/// (asleep in its run loop, 0 % CPU) when nothing can change until an input does. The main thread hands it inputs by
/// running blocks on its run loop, so nothing here takes a lock.
final class RenderThread: NSObject {
    private let loop: CoreLoop
    private let metal: MetalGlow
    private let log = Logger(subsystem: Bundle.main.bundleIdentifier ?? "app", category: "render")
    private var runLoop: CFRunLoop?
    private let started = DispatchSemaphore(value: 0)

    // Render thread only:
    private var surfaces: [OverlaySurface] = []
    private var link: CADisplayLink?
    private var linkRate: Double = 0
    private var slowTimer: Timer?
    private var pace: Int32 = RL_PACE_FULL
    private var lastFrame: CFTimeInterval = 0
    private var uploadedGradient: Int32 = -1

    init(loop: CoreLoop, metal: MetalGlow) {
        self.loop = loop
        self.metal = metal
        super.init()
        let thread = Thread { [weak self] in self?.main() }
        thread.name = "Overlay"
        thread.qualityOfService = .userInteractive
        thread.start()
        started.wait()
    }

    /// Runs a block on the render thread, waking it. Any thread.
    func perform(_ block: @escaping () -> Void) {
        guard let runLoop else { return }
        CFRunLoopPerformBlock(runLoop, CFRunLoopMode.defaultMode.rawValue, block)
        CFRunLoopWakeUp(runLoop)
    }

    /// An input changed (settings, demo, pause): a frame may be due now. Any thread.
    func inputsChanged() {
        perform { [weak self] in self?.handleInputs() }
    }

    /// Replaces the screens' overlays (main thread, after a display change). `link` comes from the main screen
    /// (NSScreen.displayLink, created on the main thread) and is added to this thread's run loop here.
    func setSurfaces(_ new: [OverlaySurface], link newLink: CADisplayLink?, refreshHz: Double, onBattery: Bool) {
        perform { [weak self] in
            guard let self else { return }
            self.link?.invalidate()
            self.link = newLink
            self.linkRate = 0
            newLink?.isPaused = true
            newLink?.add(to: .current, forMode: .default)
            let old = self.surfaces
            self.surfaces = new
            let infos = new.map(\.info)
            rl_loop_set_surfaces(self.loop.handle, infos, Int32(infos.count))
            rl_loop_set_display(self.loop.handle, refreshHz, onBattery ? 1 : 0)
            // The old panels are closed on the main thread once this thread no longer draws on them.
            DispatchQueue.main.async {
                MainActor.assumeIsolated {
                    for surface in old where !new.contains(where: { $0.panel === surface.panel }) {
                        surface.panel.close()
                    }
                }
            }
            self.pace = RL_PACE_FULL
            self.frame(due: true)
        }
    }

    // MARK: - The thread

    private func main() {
        runLoop = CFRunLoopGetCurrent()
        // A port keeps the run loop alive while no timer or link is attached (RL_PACE_BLOCK).
        RunLoop.current.add(NSMachPort(), forMode: .default)
        started.signal()
        while true {
            autoreleasepool {
                _ = RunLoop.current.run(mode: .default, before: .distantFuture)
            }
        }
    }

    @objc private func tick(_ link: CADisplayLink) {
        frame(due: true)
    }

    private func handleInputs() {
        let now = CACurrentMediaTime()
        guard rl_loop_apply_inputs(loop.handle, now) == 1 else { return }
        // At the full rate the next frame draws the change within one interval, so a stream of inputs can't push
        // frames past the cap (FramePacer.InputWaits).
        if rl_loop_input_waits(loop.handle, pace, now - lastFrame) == 1 { return }
        frame(due: false)
    }

    private func frame(due: Bool) {
        autoreleasepool {
            let now = CACurrentMediaTime()
            rl_loop_apply_inputs(loop.handle, now)
            let dt = rl_loop_frame_dt(loop.handle, now - lastFrame, due ? 1 : 0)
            lastFrame = now

            var info = RlFrameInfo()
            guard rl_loop_begin_frame(loop.handle, dt, nil, 0, 0, &info) == RL_OK else {
                log.error("A frame failed to start")
                return
            }
            if info.gradientVersion != uploadedGradient, let texels = rl_loop_gradient(loop.handle) {
                metal.upload(gradient: texels)
                uploadedGradient = info.gradientVersion
            }

            let nowMs = Int64(now * 1000)
            for index in surfaces.indices {
                var constants = RlGlowConstants()
                let action = rl_loop_plan_surface(loop.handle, Int32(index), &constants)
                if action <= RL_SURFACE_NONE { continue }
                let surface = surfaces[index]
                if action == RL_SURFACE_SHOW_AND_DRAW { show(surface.panel) }
                let presented = metal.draw(constants, on: surface.layer, inFlight: surface.inFlight)
                if rl_loop_surface_done(loop.handle, Int32(index), presented ? 1 : 0, nowMs) == 1 {
                    hide(surface.panel)
                }
            }

            var next = RlPace()
            rl_loop_end_frame(loop.handle, CACurrentMediaTime(), &next)
            wait(next)
        }
    }

    private func wait(_ next: RlPace) {
        pace = next.pace
        slowTimer?.invalidate()
        slowTimer = nil
        if next.pace == RL_PACE_FULL, let link {
            if linkRate != next.fullRateHz {
                linkRate = next.fullRateHz
                let rate = Float(next.fullRateHz)
                link.preferredFrameRateRange = CAFrameRateRange(minimum: rate, maximum: rate, preferred: rate)
            }
            link.isPaused = false
            return
        }
        link?.isPaused = true
        if next.pace == RL_PACE_SLOW || (next.pace == RL_PACE_FULL && link == nil) {
            let interval = next.pace == RL_PACE_SLOW ? next.intervalSeconds : 1 / max(next.fullRateHz, 1)
            let timer = Timer(timeInterval: interval, repeats: false) { [weak self] _ in self?.frame(due: true) }
            RunLoop.current.add(timer, forMode: .default)
            slowTimer = timer
        }
        // RL_PACE_BLOCK: nothing until an input arrives through perform.
    }

    private func show(_ panel: OverlayPanel) {
        DispatchQueue.main.async { MainActor.assumeIsolated { panel.orderFrontRegardless() } }
    }

    private func hide(_ panel: OverlayPanel) {
        DispatchQueue.main.async { MainActor.assumeIsolated { panel.orderOut(nil) } }
    }

    /// The display link's selector, for NSScreen.displayLink(target:selector:).
    static let tickSelector = #selector(RenderThread.tick(_:))
}

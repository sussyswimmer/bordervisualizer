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
    let visibility = PanelVisibility()
}

/// Drawables submitted to a screen and not yet shown. Each drawable's presented handler holds the counter, so it
/// outlives a surface that is replaced while frames are still on their way. The count is atomic; `lastAdded` belongs
/// to the render thread.
final class InFlightCounter: @unchecked Sendable {
    let counter: OpaquePointer
    private(set) var lastAdded: CFTimeInterval = 0

    init() {
        guard let counter = rlc_counter_create() else { fatalError("Out of memory") }
        self.counter = counter
    }

    deinit {
        rlc_counter_destroy(counter)
    }

    var value: Int32 { rlc_counter_load(counter) }

    func add(_ delta: Int32, at now: CFTimeInterval) {
        lastAdded = now
        rlc_counter_add(counter, delta)
    }

    func add(_ delta: Int32) {
        rlc_counter_add(counter, delta)
    }

    func reset() {
        rlc_counter_store(counter, 0)
    }
}

/// Orders a panel in and out on the main thread, in the order the render thread asked, even though a hide waits for
/// its transparent frame to reach the screen: a hide asked before the latest show is dropped. `requested` belongs to
/// the render thread, `shown` to the main thread.
final class PanelVisibility: @unchecked Sendable {
    private var requested = 0
    private var shown = 0

    /// Render thread: shows the panel now.
    func show(_ panel: OverlayPanel) {
        requested += 1
        let generation = requested
        DispatchQueue.main.async {
            MainActor.assumeIsolated {
                self.shown = generation
                panel.orderFrontRegardless()
            }
        }
    }

    /// Render thread: a hide to run once the transparent frame is on screen (or at once, for a screen that takes no
    /// frames). It does nothing if the panel was shown again meanwhile.
    func hider(_ panel: OverlayPanel) -> @Sendable () -> Void {
        let generation = requested
        return {
            DispatchQueue.main.async {
                MainActor.assumeIsolated {
                    if self.shown == generation { panel.orderOut(nil) }
                }
            }
        }
    }
}

/// The glow's render thread (doc 02, docs/10-MACOS.md §3.4): owns Metal and the loop's frame calls, and waits as
/// FramePacer says: on the main screen's display link at the full rate, on a timer at the slow rate, and not at all
/// (asleep in its run loop, 0 % CPU) when nothing can change until an input does. The main thread hands it inputs by
/// running blocks on its run loop, so nothing here takes a lock.
final class RenderThread: NSObject, @unchecked Sendable {
    private let loop: CoreLoop
    private let metal: MetalGlow
    private let tap: SystemAudioTap?
    private let samples: UnsafeMutablePointer<Float>   // the frame's drained audio, preallocated
    private let sampleCapacity: Int32
    private let log = Logger(subsystem: Bundle.main.bundleIdentifier ?? "app", category: "render")
    private var runLoop: CFRunLoop?
    private let started = DispatchSemaphore(value: 0)

    // Render thread only:
    private var surfaces: [OverlaySurface] = []
    private var link: CADisplayLink?
    private var linkRate: Double = 0
    private var timer: Timer?   // one timer, re-aimed: slow frames, and the display link's fallback
    private var pace: Int32 = RL_PACE_FULL
    private var lastFrame: CFTimeInterval = 0
    private var uploadedGradient: Int32 = -1
    private var capturing = false
    private var captureGeneration: Int32 = 0

    init(loop: CoreLoop, metal: MetalGlow, tap: SystemAudioTap?) {
        self.loop = loop
        self.metal = metal
        self.tap = tap
        sampleCapacity = tap.map { rlc_ring_capacity($0.ring) } ?? 1
        samples = UnsafeMutablePointer<Float>.allocate(capacity: Int(sampleCapacity))
        super.init()
        let thread = Thread { [weak self] in self?.main() }
        thread.name = "Overlay"
        thread.qualityOfService = .userInteractive
        thread.start()
        started.wait()
    }

    deinit {
        samples.deallocate()
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
            rl_loop_set_surfaces(self.loop.handle, infos, Int32(infos.count), CACurrentMediaTime())
            rl_loop_set_display(self.loop.handle, refreshHz, onBattery ? 1 : 0)
            // The old panels are closed on the main thread once this thread no longer draws on them.
            let closing = old.map(\.panel).filter { panel in !new.contains { $0.panel === panel } }
            DispatchQueue.main.async {
                MainActor.assumeIsolated {
                    for panel in closing { panel.close() }
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
        let timer = Timer(fire: .distantFuture, interval: 3600, repeats: true) { [weak self] _ in
            self?.frame(due: true)
        }
        RunLoop.current.add(timer, forMode: .default)
        self.timer = timer
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

            // The audio since the last frame (doc 02 step 1). Samples from before the tap last opened, closed or
            // restarted are dropped with the analysis they belonged to.
            var count: Int32 = 0
            var rate: Int32 = 0
            if let tap {
                count = rlc_ring_read(tap.ring, samples, sampleCapacity)
                let generation = tap.generation
                if generation != captureGeneration {
                    captureGeneration = generation
                    count = 0
                }
                rate = tap.sampleRate
            }
            var info = RlFrameInfo()
            guard rl_loop_begin_frame(loop.handle, dt, samples, count, rate, captureGeneration, &info) == RL_OK else {
                log.error("A frame failed to start")
                return
            }
            // The tap runs only while the glow listens (music sync, on, not paused or in the demo).
            let listen = info.listening == 1
            if listen != capturing {
                capturing = listen
                tap?.setActive(listen)
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
                if action == RL_SURFACE_SHOW_AND_DRAW { surface.visibility.show(surface.panel) }
                // A transparent frame hides its window once it is on screen, so showing the window again never
                // flashes the old glow (Windows hides only after its transparent present, too).
                let hide = action == RL_SURFACE_CLEAR ? surface.visibility.hider(surface.panel) : nil
                let presented = metal.draw(constants, on: surface.layer, inFlight: surface.inFlight, now: now,
                                           onScreen: hide)
                if rl_loop_surface_done(loop.handle, Int32(index), presented ? 1 : 0, nowMs) == 1, !presented {
                    hide?() // a screen that takes no frames (asleep) is hidden without its transparent frame
                }
            }

            var next = RlPace()
            rl_loop_end_frame(loop.handle, CACurrentMediaTime(), &next)
            wait(next)
        }
    }

    /// Display link at the full rate, with the timer as a fallback should the main screen stop refreshing (turned
    /// off, asleep: the link then stops firing, as Windows' vsync wait stops); the timer alone at the slow rate; and
    /// nothing at all when blocked, until an input arrives through `perform`. The one timer is re-aimed, never made.
    private func wait(_ next: RlPace) {
        pace = next.pace
        let now = Date()
        switch next.pace {
        case RL_PACE_FULL where link != nil:
            if let link {
                if linkRate != next.fullRateHz {
                    linkRate = next.fullRateHz
                    let rate = Float(next.fullRateHz)
                    link.preferredFrameRateRange = CAFrameRateRange(minimum: rate, maximum: rate, preferred: rate)
                }
                link.isPaused = false
            }
            timer?.fireDate = now.addingTimeInterval(Self.linkFallbackSeconds)
        case RL_PACE_FULL:
            timer?.fireDate = now.addingTimeInterval(1 / max(next.fullRateHz, 1))
        case RL_PACE_SLOW:
            link?.isPaused = true
            timer?.fireDate = now.addingTimeInterval(next.intervalSeconds)
        default:
            link?.isPaused = true
            timer?.fireDate = .distantFuture
        }
    }

    /// No refresh for this long at the full rate: draw anyway (Windows' VsyncFallbackSeconds).
    static let linkFallbackSeconds: TimeInterval = 0.1

    /// The display link's selector, for NSScreen.displayLink(target:selector:).
    static let tickSelector = #selector(RenderThread.tick(_:))
}

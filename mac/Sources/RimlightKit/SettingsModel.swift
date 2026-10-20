import AppKit
import CRimlight
import Metal
import SwiftUI

/// One preset as the Settings window and the menu show it (doc 06 §1): its name and its two colours.
struct PresetChoice: Identifiable {
    let index: Int32
    let name: String
    let primary: Color
    let secondary: Color
    var id: Int32 { index }
}

/// The settings as the Settings window and the menu edit them (doc 06 §3): every change applies to the glow at once
/// and is saved 500 ms after the last one, through Core's store. Main thread.
@MainActor
final class SettingsModel: ObservableObject {
    static let saveDelay: TimeInterval = 0.5

    @Published var settings: AppSettings {
        didSet { if settings != oldValue && !reloading { changed() } }
    }
    /// What's playing (M4), for the Color page.
    @Published private(set) var nowPlaying: NowPlayingService.Track?
    /// Music refused the cover (Automation not allowed).
    @Published private(set) var automationDenied = false
    /// The album colours the glow has (sRGB), or nil.
    @Published private(set) var albumColors: (Color, Color)?

    let loop: CoreLoop
    let device: MTLDevice
    let presets: [PresetChoice]
    /// The Motion page's level meter, observed by the meter alone so its 30 Hz updates redraw nothing else.
    let meter: MeterModel
    private let tap: SystemAudioTap?
    private let onChange: () -> Void
    private var reloading = false
    private var saveWork: DispatchWorkItem?
    private var audioUsers = 0
    private var previewGlow: MetalGlow?

    init(loop: CoreLoop, device: MTLDevice, tap: SystemAudioTap?, onChange: @escaping () -> Void) {
        self.loop = loop
        self.device = device
        self.tap = tap
        self.onChange = onChange
        settings = loop.settings ?? AppSettings.fallback
        presets = Self.loadPresets(from: loop.settings)
        meter = MeterModel(loop: loop, tap: tap)
        meter.audio = self
    }

    /// A binding to one field. `$model.settings.animation` would find SwiftUI's `Binding.animation(_:)` instead.
    func binding<Value>(_ key: WritableKeyPath<AppSettings, Value>) -> Binding<Value> {
        Binding(get: { self.settings[keyPath: key] }, set: { self.settings[keyPath: key] = $0 })
    }

    /// The preview's GPU side, made once and kept, so switching pages doesn't compile the shader again.
    var previewMetal: MetalGlow? {
        if previewGlow == nil { previewGlow = try? MetalGlow(device: device) }
        return previewGlow
    }

    /// Whether the glow uses album colours, for the media service (covers are read only then).
    var usesAlbumColors: Bool {
        settings.colorMode == "AlbumArt" && !settings.overrideAlbumColor
    }

    /// A new track or cover from the media service.
    func mediaChanged(_ track: NowPlayingService.Track?, automationDenied denied: Bool) {
        nowPlaying = track
        automationDenied = denied
        var primary = RlRgb(), secondary = RlRgb()
        albumColors = rl_album_palette(loop.handle, &primary, &secondary) == 1
            ? (Color(linear: primary), Color(linear: secondary)) : nil
    }

    /// The window's visibility, from its occlusion state: the meter runs only while it is on screen.
    func setWindowVisible(_ visible: Bool) {
        meter.windowVisible = visible
    }

    /// Applies a preset's look (doc 06 §1: its appearance fields only).
    func apply(preset: PresetChoice) {
        guard let json = settings.encoded() else { return }
        let applied = json.withCString { source in
            Core.readString { rl_preset_apply(preset.index, source, $0, $1) }
        }
        if let next = applied.flatMap(AppSettings.decode) { settings = next }
    }

    /// Writes any pending change now (on quit).
    func saveNow() {
        guard saveWork != nil else { return }
        saveWork?.cancel()
        save()
    }

    /// The meter and the preview want the audio: the capture stays open while either shows it. Calls pair up.
    func acquireAudio() {
        audioUsers += 1
        guard audioUsers == 1 else { return }
        loop.setAudioWanted(true)
        onChange() // wakes a stopped render loop
    }

    func releaseAudio() {
        guard audioUsers > 0 else { return }
        audioUsers -= 1
        guard audioUsers == 0 else { return }
        loop.setAudioWanted(false)
        onChange()
    }

    /// Lets the audio tap open once the first-run banner has been read (or the window closed).
    func finishFirstRun() {
        guard !settings.firstRunComplete else { return }
        settings.firstRunComplete = true
        tap?.setAllowed(true)
    }

    private func changed() {
        loop.apply(settings, save: false)
        onChange()
        saveWork?.cancel()
        let work = DispatchWorkItem { [weak self] in
            MainActor.assumeIsolated { self?.save() }
        }
        saveWork = work
        DispatchQueue.main.asyncAfter(deadline: .now() + Self.saveDelay, execute: work)
    }

    // Core's store validates what it writes; the window then shows exactly what the file holds.
    private func save() {
        saveWork = nil
        loop.apply(settings, save: true)
        if let saved = loop.settings, saved != settings {
            reloading = true
            settings = saved
            reloading = false
            onChange() // the corrected values reach the overlays too
        }
    }

    private static func loadPresets(from base: AppSettings?) -> [PresetChoice] {
        guard let json = (base ?? AppSettings.fallback).encoded() else { return [] }
        return (0..<max(rl_presets_count(), 0)).compactMap { index in
            let name = Core.readString { rl_preset_name(index, $0, $1) }
            let applied = json.withCString { source in Core.readString { rl_preset_apply(index, source, $0, $1) } }
            guard let name, let look = applied.flatMap(AppSettings.decode) else { return nil }
            return PresetChoice(index: index, name: name, primary: Color(hex: look.primaryHex),
                                secondary: Color(hex: look.secondaryHex))
        }
    }
}

extension Color {
    /// An sRGB colour from "#RRGGBB" (grey for anything else).
    init(hex: String) {
        let trimmed = hex.trimmingCharacters(in: .whitespaces)
        let digits = trimmed.dropFirst(trimmed.hasPrefix("#") ? 1 : 0)
        guard digits.count == 6, digits.allSatisfy(\.isHexDigit), let value = UInt32(digits, radix: 16) else {
            self = .gray
            return
        }
        self.init(.sRGB, red: Double(value >> 16 & 0xFF) / 255, green: Double(value >> 8 & 0xFF) / 255,
                  blue: Double(value & 0xFF) / 255)
    }

    /// "#RRGGBB" in sRGB, as the settings file stores colours; nil if it has no sRGB form.
    var hex: String? {
        guard let color = NSColor(self).usingColorSpace(.sRGB) else { return nil }
        func byte(_ c: CGFloat) -> Int { Int((min(max(c, 0), 1) * 255).rounded()) }
        return String(format: "#%02X%02X%02X", byte(color.redComponent), byte(color.greenComponent),
                      byte(color.blueComponent))
    }
}

extension Color {
    /// A colour from Core's linear RGB (CORE-API §2), encoded to sRGB for display.
    init(linear c: RlRgb) {
        func encode(_ v: Float) -> Double {
            let x = Double(min(max(v, 0), 1))
            return x <= 0.0031308 ? 12.92 * x : 1.055 * pow(x, 1 / 2.4) - 0.055
        }
        self.init(.sRGB, red: encode(c.r), green: encode(c.g), blue: encode(c.b))
    }
}

extension AppSettings {
    /// Core's defaults (CORE-API §7.1), only if the library can't be read.
    static let fallback = AppSettings(
        version: 1, enabled: true, animation: "MusicSync", whenSilent: "IdleGlow", colorMode: "AlbumArt",
        overrideAlbumColor: false, primaryHex: "#7C5CFF", secondaryHex: "#22D3EE", primaryRatio: 0.6,
        coreThicknessDip: 6, glow: 0.45, brightness: 0.8, sensitivity: 1, cornerRadiusDip: 0, coverTaskbar: true,
        monitors: "All", customMonitorIds: [], fpsCap: 60, pauseInFullscreen: true, onBattery: "Reduce",
        hideFromScreenCapture: false, launchAtStartup: true, toggleHotkey: "Ctrl+Alt+L", autoUpdate: true,
        firstRunComplete: false)
}

/// The Motion page's level meter (doc 06 §3.3): the analyzer's latest loudness and beat, read 30 times a second only
/// while the page is shown and the window is on screen. SwiftUI doesn't report a window closing as the page
/// disappearing, so the window's occlusion state is the other half of the rule.
@MainActor
final class MeterModel: ObservableObject {
    @Published private(set) var level: Double = 0
    @Published private(set) var beat: Double = 0
    @Published private(set) var cantHear = false

    weak var audio: SettingsModel?
    var pageShown = false { didSet { follow() } }
    var windowVisible = false { didSet { follow() } }
    private let loop: CoreLoop
    private let tap: SystemAudioTap?
    private var timer: Timer?

    init(loop: CoreLoop, tap: SystemAudioTap?) {
        self.loop = loop
        self.tap = tap
    }

    private func follow() {
        let run = pageShown && windowVisible
        if run && timer == nil {
            audio?.acquireAudio()
            let timer = Timer(timeInterval: 1.0 / 30, repeats: true) { [weak self] _ in
                MainActor.assumeIsolated { self?.read() }
            }
            RunLoop.main.add(timer, forMode: .common)
            self.timer = timer
            read()
        } else if !run, let timer {
            timer.invalidate()
            self.timer = nil
            audio?.releaseAudio()
        }
    }

    // Publishes only what changed, so an idle meter redraws nothing.
    private func read() {
        let reading = loop.audio
        let newLevel = reading.sequence >= 0 ? (Double(reading.level) * 100).rounded() / 100 : 0
        let newBeat = reading.sequence >= 0 ? (Double(reading.beat) * 20).rounded() / 20 : 0
        let newCantHear = tap?.cantHear == true
        if newLevel != level { level = newLevel }
        if newBeat != beat { beat = newBeat }
        if newCantHear != cantHear { cantHear = newCantHear }
    }
}

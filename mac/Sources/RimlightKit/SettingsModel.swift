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
/// and is saved 500 ms after the last one, through Core's store. Also runs the Motion page's level meter. Main thread.
@MainActor
final class SettingsModel: ObservableObject {
    static let saveDelay: TimeInterval = 0.5

    @Published var settings: AppSettings {
        didSet { if settings != oldValue && !reloading { changed() } }
    }
    @Published private(set) var level: Double = 0
    @Published private(set) var beat: Double = 0
    @Published private(set) var cantHear = false

    let loop: CoreLoop
    let device: MTLDevice
    let presets: [PresetChoice]
    private let tap: SystemAudioTap?
    private let onChange: () -> Void
    private var reloading = false
    private var saveWork: DispatchWorkItem?
    private var meter: Timer?
    private var meterUsers = 0

    init(loop: CoreLoop, device: MTLDevice, tap: SystemAudioTap?, onChange: @escaping () -> Void) {
        self.loop = loop
        self.device = device
        self.tap = tap
        self.onChange = onChange
        settings = loop.settings ?? AppSettings.fallback
        presets = Self.loadPresets(from: loop.settings)
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

    /// The level meter and the preview want the audio; the capture stays open while anything shows them.
    func startMeter() {
        meterUsers += 1
        guard meterUsers == 1 else { return }
        loop.setAudioWanted(true)
        let timer = Timer(timeInterval: 1.0 / 30, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated { self?.readMeter() }
        }
        RunLoop.main.add(timer, forMode: .common)
        meter = timer
        onChange()
    }

    func stopMeter() {
        guard meterUsers > 0 else { return }
        meterUsers -= 1
        guard meterUsers == 0 else { return }
        meter?.invalidate()
        meter = nil
        loop.setAudioWanted(false)
        onChange()
    }

    /// Lets the audio tap open once the first-run banner has been read.
    func finishFirstRun() {
        if !settings.firstRunComplete { settings.firstRunComplete = true }
        tap?.setAllowed(true)
    }

    private func readMeter() {
        let reading = loop.audio
        level = reading.sequence >= 0 ? Double(reading.level) : 0
        beat = reading.sequence >= 0 ? Double(reading.beat) : 0
        cantHear = tap?.cantHear == true
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
        let digits = hex.trimmingCharacters(in: .whitespaces).dropFirst(hex.hasPrefix("#") ? 1 : 0)
        guard digits.count == 6, let value = UInt32(digits, radix: 16) else {
            self = .gray
            return
        }
        self.init(.sRGB, red: Double(value >> 16 & 0xFF) / 255, green: Double(value >> 8 & 0xFF) / 255,
                  blue: Double(value & 0xFF) / 255)
    }

    /// "#RRGGBB" in sRGB, as the settings file stores colours.
    var hex: String {
        guard let color = NSColor(self).usingColorSpace(.sRGB) else { return "#7C5CFF" }
        func byte(_ c: CGFloat) -> Int { Int((min(max(c, 0), 1) * 255).rounded()) }
        return String(format: "#%02X%02X%02X", byte(color.redComponent), byte(color.greenComponent),
                      byte(color.blueComponent))
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

import AppKit
import SwiftUI

/// The Settings window (doc 06 §3): the same six pages as on Windows. Every change applies live, with no Save button.
struct SettingsView: View {
    enum Page: String, CaseIterable, Identifiable, Hashable {
        case appearance, color, motion, displays, behavior, about
        var id: String { rawValue }

        var title: String {
            switch self {
            case .appearance: "Appearance"
            case .color: "Color"
            case .motion: "Motion"
            case .displays: "Displays"
            case .behavior: "Behavior"
            case .about: "About"
            }
        }

        var symbol: String {
            switch self {
            case .appearance: "paintbrush"
            case .color: "paintpalette"
            case .motion: "waveform"
            case .displays: "display.2"
            case .behavior: "gearshape"
            case .about: "info.circle"
            }
        }
    }

    @ObservedObject var model: SettingsModel
    @State private var page: Page? = .appearance

    var body: some View {
        NavigationSplitView {
            List(Page.allCases, selection: $page) { page in
                Label(page.title, systemImage: page.symbol).tag(page)
            }
            .navigationSplitViewColumnWidth(min: 160, ideal: 170)
        } detail: {
            VStack(spacing: 0) {
                if !model.settings.firstRunComplete { WelcomeBanner(model: model) }
                switch page ?? .appearance {
                case .appearance: AppearancePage(model: model)
                case .color: ColorPage(model: model)
                case .motion: MotionPage(model: model)
                case .displays: DisplaysPage(model: model)
                case .behavior: BehaviorPage(model: model)
                case .about: AboutPage(model: model)
                }
            }
        }
        .frame(minWidth: 720, minHeight: 520)
    }
}

/// The first run (PRD §7): what to do, and why macOS is about to ask for "System Audio Recording" (10-MACOS §2.1).
/// The audio tap stays closed until it is dismissed, so the prompt comes after this explanation.
struct WelcomeBanner: View {
    @ObservedObject var model: SettingsModel

    var body: some View {
        HStack(alignment: .top, spacing: 12) {
            Image(systemName: "sparkles").font(.title2).foregroundStyle(.tint)
            VStack(alignment: .leading, spacing: 6) {
                Text("Play some music, then look at the edges of your screen.").font(.headline)
                Text("To hear the music, \(Core.appName) needs to listen to your Mac's sound output, so macOS will ask you to allow System Audio Recording. It never uses the microphone, and nothing is recorded or saved.")
                    .font(.callout).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
                Button("Start") { model.finishFirstRun() }.keyboardShortcut(.defaultAction)
            }
        }
        .padding(16)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(.tint.opacity(0.08))
    }
}

// MARK: - Pages

struct AppearancePage: View {
    @ObservedObject var model: SettingsModel

    /// What Thickness sets in each edge style (doc 06 §3.1): the soft part along the edges, or the line's width.
    private var thicknessHelp: String {
        model.settings.edgeStyle == "Line"
            ? "How wide the bright line along the edges is."
            : "How wide the soft, bright part along the edges is. More keeps the light bright further in."
    }

    var body: some View {
        Form {
            Section {
                GlowPreviewView(model: model)
                    .aspectRatio(16 / 9, contentMode: .fit)
                    .frame(maxWidth: 420)
                    .frame(maxWidth: .infinity)
            }
            Section {
                // The soft glow, or a bright line along the very edge with a dimmer glow inside it (Maxwell's
                // request); on a screen with a notch either goes round it.
                Picker("Edge", selection: $model.settings.edgeStyle) {
                    Text("Soft glow").tag("Glow")
                    Text("Bright line").tag("Line")
                }
                .pickerStyle(.segmented)
                PercentSlider("Thickness", value: $model.settings.coreThicknessDip, range: 0...40, step: 1,
                              format: { "\(Int($0)) pt" })
                    .help(thicknessHelp)
                PercentSlider("Glow", value: $model.settings.glow, range: 0...1)
                PercentSlider("Brightness", value: $model.settings.brightness, range: 0.1...1)
                PercentSlider("Corner radius", value: $model.settings.cornerRadiusDip, range: 0...40, step: 1,
                              format: { "\(Int($0)) pt" })
                Toggle("Cover the menu bar and Dock", isOn: $model.settings.coverTaskbar)
            }
            Section("Presets") {
                HStack(spacing: 14) {
                    ForEach(model.presets) { preset in
                        Button { model.apply(preset: preset) } label: {
                            VStack(spacing: 4) {
                                Circle()
                                    .fill(LinearGradient(colors: [preset.primary, preset.secondary],
                                                         startPoint: .topLeading, endPoint: .bottomTrailing))
                                    .frame(width: 34, height: 34)
                                Text(preset.name).font(.caption)
                            }
                        }
                        .buttonStyle(.plain)
                        .accessibilityLabel("\(preset.name) preset")
                    }
                }
            }
        }
        .formStyle(.grouped)
    }
}

struct ColorPage: View {
    @ObservedObject var model: SettingsModel

    var body: some View {
        Form {
            Section {
                Picker("Colors", selection: $model.settings.colorMode) {
                    Text("From album art").tag("AlbumArt")
                    Text("Manual").tag("Manual")
                }
                .pickerStyle(.segmented)
                Toggle("Use my colors even when there's album art", isOn: $model.settings.overrideAlbumColor)
                    .disabled(model.settings.colorMode != "AlbumArt")
            }
            Section {
                if let track = model.nowPlaying, let title = track.title {
                    HStack {
                        VStack(alignment: .leading) {
                            Text(title).lineLimit(1)
                            Text([track.artist, track.app].compactMap { $0 }.joined(separator: " · "))
                                .font(.caption).foregroundStyle(.secondary).lineLimit(1)
                        }
                        Spacer()
                        if model.usesAlbumColors, let colors = model.albumColors {
                            Circle().fill(colors.0).frame(width: 18, height: 18).accessibilityLabel("Primary album color")
                            Circle().fill(colors.1).frame(width: 18, height: 18)
                                .accessibilityLabel("Secondary album color")
                        }
                    }
                } else {
                    Text("Nothing playing in Music or Spotify.").foregroundStyle(.secondary)
                }
                if model.automationDenied {
                    Label("Allow \(Core.appName) to read \(model.nowPlaying?.app ?? "Music")'s album art in Privacy & Security › Automation.",
                          systemImage: "exclamationmark.triangle").foregroundStyle(.orange)
                    Button("Open Privacy & Security") { AudioPrivacy.openAutomation() }
                }
            } header: {
                Text("Now playing")
            } footer: {
                Text("Album colors come from Music and Spotify (Spotify's cover is downloaded from Spotify). Other players use your colors.")
                    .font(.caption).foregroundStyle(.secondary)
            }
            Section("My colors") {
                ColorPicker("Primary", selection: hexBinding(\.primaryHex), supportsOpacity: false)
                ColorPicker("Secondary", selection: hexBinding(\.secondaryHex), supportsOpacity: false)
                PercentSlider("Primary share", value: $model.settings.primaryRatio, range: 0.1...0.9)
            }
        }
        .formStyle(.grouped)
    }

    private func hexBinding(_ key: WritableKeyPath<AppSettings, String>) -> Binding<Color> {
        Binding(get: { Color(hex: model.settings[keyPath: key]) },
                set: { if let hex = $0.hex { model.settings[keyPath: key] = hex } })
    }
}

struct MotionPage: View {
    @ObservedObject var model: SettingsModel

    /// What the chosen movement does (doc 06 §3.3), in the Windows Motion page's words; outside Music Sync, where the
    /// light doesn't follow the music, that it changes nothing there (as When it's quiet).
    private var movementHelp: String {
        if model.settings.animation != "MusicSync" { return "Only matters in Music Sync." }
        return model.settings.movement == "Pulse"
            ? "The whole edge swells and brightens together on each beat."
            : "Light travels round the edge: comets chase round it, the colors stream round it and each beat ripples along it."
    }

    var body: some View {
        Form {
            Section {
                Picker("Mode", selection: model.binding(\.animation)) {
                    Text("Music Sync").tag("MusicSync")
                    Text("Idle Glow").tag("IdleGlow")
                    Text("Off").tag("Off")
                }
                // How the light moves with the music (Maxwell's request): light travelling round the edge, the
                // default, or the whole edge pulsing on each beat as before.
                Picker("Movement", selection: $model.settings.movement) {
                    Text("Flow").tag("Flow")
                    Text("Pulse").tag("Pulse")
                }
                .pickerStyle(.segmented)
                .disabled(model.settings.animation != "MusicSync")
                Text(movementHelp)
                    .font(.caption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
                Picker("When it's quiet", selection: $model.settings.whenSilent) {
                    Text("Keep a soft glow").tag("IdleGlow")
                    Text("Hide the glow").tag("Hide")
                }
                .disabled(model.settings.animation != "MusicSync")
                PercentSlider("Sensitivity", value: $model.settings.sensitivity, range: 0.25...2,
                              format: { String(format: "%.2f×", $0) })
            }
            Section("Sound") {
                MeterSection(meter: model.meter)
            }
        }
        .formStyle(.grouped)
        .onAppear { model.meter.pageShown = true }
        .onDisappear { model.meter.pageShown = false }
    }
}

/// The level meter and the "can't hear" warning: the only views that follow the 30 Hz meter.
struct MeterSection: View {
    @ObservedObject var meter: MeterModel

    var body: some View {
        LevelMeter(level: meter.level, beat: meter.beat)
            .frame(height: 10)
            .accessibilityLabel("Sound level")
            .accessibilityValue("\(Int(meter.level * 100)) percent")
        if meter.cantHear {
            Label("\(Core.appName) can't hear your Mac. Allow it under System Audio Recording Only.",
                  systemImage: "exclamationmark.triangle")
                .foregroundStyle(.orange)
            Button("Open Privacy & Security") { AudioPrivacy.open() }
        }
    }
}

struct DisplaysPage: View {
    @ObservedObject var model: SettingsModel
    @State private var screens: [DisplayChoice] = []

    var body: some View {
        Form {
            Section {
                Picker("Show the glow on", selection: $model.settings.monitors) {
                    Text("All displays").tag("All")
                    Text("The main display").tag("PrimaryOnly")
                    Text("The displays I choose").tag("Custom")
                }
                if model.settings.monitors == "Custom" {
                    ForEach(screens) { screen in
                        Toggle(screen.name, isOn: binding(for: screen))
                    }
                }
            }
            Section {
                Picker("Frame rate limit", selection: $model.settings.fpsCap) {
                    Text("30 fps").tag(30)
                    Text("60 fps").tag(60)
                    Text("120 fps").tag(120)
                    Text("Display's rate").tag(0)
                }
            }
        }
        .formStyle(.grouped)
        .onAppear { screens = DisplayChoice.current() }
        .onReceive(NotificationCenter.default.publisher(for: NSApplication.didChangeScreenParametersNotification)) { _ in
            screens = DisplayChoice.current()
        }
    }

    private func binding(for screen: DisplayChoice) -> Binding<Bool> {
        Binding(
            get: { model.settings.customMonitorIds.contains { $0.caseInsensitiveCompare(screen.id) == .orderedSame } },
            set: { on in
                var ids = model.settings.customMonitorIds.filter { $0.caseInsensitiveCompare(screen.id) != .orderedSame }
                if on { ids.append(screen.id) }
                model.settings.customMonitorIds = ids
            })
    }
}

struct BehaviorPage: View {
    @ObservedObject var model: SettingsModel

    var body: some View {
        Form {
            Section {
                Toggle("Pause for full-screen videos and games", isOn: $model.settings.pauseInFullscreen)
                Picker("On battery", selection: $model.settings.onBattery) {
                    Text("Keep going").tag("Normal")
                    Text("Save power (30 fps)").tag("Reduce")
                    Text("Pause the glow").tag("Pause")
                }
            } footer: {
                Text("In Low Power Mode the glow always saves power.").font(.caption).foregroundStyle(.secondary)
            }
            Section {
                Toggle("Open at login", isOn: $model.settings.launchAtStartup)
                if model.loginNeedsApproval && model.settings.launchAtStartup {
                    Label("Allow \(Core.appName) in Login Items to open it at login.", systemImage: "exclamationmark.triangle")
                        .foregroundStyle(.orange)
                    Button("Open Login Items") { LoginItem.openSettings() }
                }
                ShortcutRecorder(model: model)
            }
            Section {
                Toggle("Hide the glow from screen recordings", isOn: $model.settings.hideFromScreenCapture)
            } footer: {
                Text("Some recording apps may still show it.").font(.caption).foregroundStyle(.secondary)
            }
        }
        .formStyle(.grouped)
    }
}

struct AboutPage: View {
    @ObservedObject var model: SettingsModel

    var body: some View {
        Form {
            Section {
                HStack(spacing: 14) {
                    Image(nsImage: NSApp.applicationIconImage).resizable().frame(width: 56, height: 56)
                    VStack(alignment: .leading) {
                        Text(Core.appName).font(.title2.bold())
                        Text("Version \(Core.version)").foregroundStyle(.secondary)
                    }
                }
                Link("Source code on GitHub", destination: URL(string: "https://github.com/sussyswimmer/bordervisualizer")!)
                Text("Free and open source under the MIT License.").foregroundStyle(.secondary)
            }
            if let checkForUpdates = model.checkForUpdates {
                Section {
                    Toggle("Check for updates automatically", isOn: $model.settings.autoUpdate)
                    Button("Check for Updates…") { checkForUpdates() }
                } footer: {
                    Text("Updates download in the background and install when \(Core.appName) quits.")
                        .font(.caption).foregroundStyle(.secondary)
                }
            }
            Section("Privacy") {
                Text("Only your Mac's sound output is captured, through a system audio tap. The microphone is never opened.")
                Text("Audio is analyzed in memory and immediately discarded. Nothing is recorded or saved.")
                Text(model.checkForUpdates == nil
                     ? "No telemetry or analytics. The only network request is Spotify's album cover, from Spotify."
                     : "No telemetry or analytics. The only network requests are the update check, to GitHub, and Spotify's album cover, from Spotify.")
            }
            Section {
                Button("Show the Settings File in Finder") { AppPaths.revealSettingsFile() }
            }
        }
        .formStyle(.grouped)
    }
}

// MARK: - Pieces

/// A labelled slider with its value shown as a percentage (or a custom format).
struct PercentSlider: View {
    let title: String
    @Binding var value: Double
    let range: ClosedRange<Double>
    let step: Double?
    let format: (Double) -> String

    init(_ title: String, value: Binding<Double>, range: ClosedRange<Double>, step: Double? = nil,
         format: @escaping (Double) -> String = { "\(Int(($0 * 100).rounded())) %" }) {
        self.title = title
        _value = value
        self.range = range
        self.step = step
        self.format = format
    }

    var body: some View {
        LabeledContent(title) {
            HStack {
                if let step {
                    Slider(value: $value, in: range, step: step).accessibilityLabel(title)
                } else {
                    Slider(value: $value, in: range).accessibilityLabel(title)
                }
                Text(format(value)).monospacedDigit().frame(width: 52, alignment: .trailing)
            }
        }
    }
}

/// A small level meter: the loudness as a bar, brightening on each beat.
struct LevelMeter: View {
    let level: Double
    let beat: Double

    var body: some View {
        GeometryReader { geometry in
            ZStack(alignment: .leading) {
                Capsule().fill(.quaternary)
                Capsule().fill(.tint.opacity(0.55 + 0.45 * beat))
                    .frame(width: max(geometry.size.width * level, 0))
            }
        }
    }
}

/// One display for the Displays page: its name and stable UUID.
struct DisplayChoice: Identifiable {
    let id: String
    let name: String

    @MainActor
    static func current() -> [DisplayChoice] {
        NSScreen.screens.compactMap { screen in
            OverlayController.uuid(of: screen).map { DisplayChoice(id: $0, name: screen.localizedName) }
        }
    }
}

/// Privacy & Security › Screen & System Audio Recording, where "System Audio Recording Only" lists the app.
enum AudioPrivacy {
    @MainActor
    static func open() {
        if let url = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_ScreenCapture") {
            NSWorkspace.shared.open(url)
        }
    }

    /// Privacy & Security › Automation, where "control Music" is allowed.
    @MainActor
    static func openAutomation() {
        if let url = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_Automation") {
            NSWorkspace.shared.open(url)
        }
    }
}

/// The glow's global shortcut (doc 06 §3.5): shows it, records a new one from the next key pressed, or removes it.
struct ShortcutRecorder: View {
    @ObservedObject var model: SettingsModel
    @State private var recording = false
    @State private var monitor: Any?
    @State private var refused = false

    var body: some View {
        LabeledContent("Glow on and off") {
            HStack {
                Text(recording ? "Press a shortcut…" : current).monospaced()
                    .foregroundStyle(recording ? .secondary : .primary)
                Button(recording ? "Cancel" : "Record") { recording ? stop() : start() }
                if !model.settings.toggleHotkey.isEmpty && !recording {
                    Button("Remove") { model.settings.toggleHotkey = "" }
                }
            }
        }
        // A monitor left running would swallow every key in the app, so recording ends with the view or the focus.
        .onDisappear { stop() }
        .onReceive(NotificationCenter.default.publisher(for: NSWindow.didResignKeyNotification)) { _ in stop() }
        if refused {
            Text("Use ⌃ or ⌘ with a key, or a function key. ⌘Q, ⌘W, ⌘H, ⌘M, ⌘Tab and ⌘Space stay with macOS.").font(.caption).foregroundStyle(.secondary)
        }
        if !recording, let problem = model.hotkeyProblem {
            Label(problem == .taken ? "Another app uses this shortcut. Choose another."
                                    : "This shortcut doesn't work on a Mac. Record another.",
                  systemImage: "exclamationmark.triangle")
                .foregroundStyle(.orange)
        }
    }

    private var current: String {
        let setting = model.settings.toggleHotkey
        if setting.isEmpty { return "None" }
        return Shortcut(setting: setting)?.display ?? setting
    }

    private func start() {
        guard monitor == nil else { return }
        refused = false
        recording = true
        model.recordingShortcut(true)
        monitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { event in
            if event.keyCode == 53 /* kVK_Escape */ && event.modifierFlags.intersection(.deviceIndependentFlagsMask).isEmpty {
                stop()
                return nil
            }
            if let shortcut = Shortcut(keyCode: event.keyCode, flags: event.modifierFlags), let text = shortcut.setting {
                model.settings.toggleHotkey = text
                stop()
            } else {
                refused = true
            }
            return nil // the key belongs to the recorder
        }
    }

    private func stop() {
        guard let monitor else { return }
        NSEvent.removeMonitor(monitor)
        self.monitor = nil
        recording = false
        model.recordingShortcut(false)
    }
}

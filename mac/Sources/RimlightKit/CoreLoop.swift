import CRimlight
import Foundation

/// Core's overlay loop (rimlight_core.h, "Overlay loop"): the glow's per-frame decisions. Settings, demo and pause from
/// any thread; the frame calls are made by RenderThread only.
final class CoreLoop {
    let handle: RlLoop

    init?(settingsDirectory: String, demo: Bool) {
        let handle = settingsDirectory.withCString { rl_loop_create($0, demo ? 1 : 0) }
        guard handle != 0 else { return nil }
        self.handle = handle
    }

    deinit {
        rl_loop_destroy(handle)
    }

    var settings: AppSettings? {
        Core.readString { rl_loop_settings(handle, $0, $1) }.flatMap(AppSettings.decode)
    }

    /// Publishes the settings to the glow and writes settings.json. Returns RL_OK or a negative status.
    @discardableResult
    func apply(_ settings: AppSettings, save: Bool = true) -> Int32 {
        guard let json = settings.encoded() else { return RL_BAD_JSON }
        return json.withCString { rl_loop_apply_settings(handle, $0, save ? 1 : 0) }
    }

    var demo: Bool {
        get { rl_loop_demo(handle) == 1 }
        set { rl_loop_set_demo(handle, newValue ? 1 : 0) }
    }

    func setPaused(_ paused: Bool) {
        rl_loop_set_paused(handle, paused ? 1 : 0)
    }
}

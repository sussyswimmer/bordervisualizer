import AppKit
import Carbon.HIToolbox
import CRimlight

/// A shortcut as the settings store it ("Ctrl+Alt+L", the Windows format, H-009) and as the Mac registers it.
struct Shortcut: Equatable {
    let modifiers: Int32  // RL_HOTKEY_* flags
    let virtualKey: Int32 // the Windows virtual-key code the setting names

    /// The setting's text parsed by Core (the Windows rules); nil for "" or an invalid shortcut.
    init?(setting: String) {
        var modifiers: Int32 = 0, key: Int32 = 0
        guard setting.withCString({ rl_hotkey_parse($0, &modifiers, &key) }) == 1 else { return nil }
        self.modifiers = modifiers
        virtualKey = key
    }

    /// A pressed key (Carbon key code and AppKit flags), if it has a name and the Mac allows it.
    init?(keyCode: UInt16, flags: NSEvent.ModifierFlags) {
        guard let key = KeyCodes.virtualKey(forCarbon: Int(keyCode)) else { return nil }
        var modifiers: Int32 = 0
        if flags.contains(.control) { modifiers |= RL_HOTKEY_CTRL }
        if flags.contains(.option) { modifiers |= RL_HOTKEY_ALT }
        if flags.contains(.shift) { modifiers |= RL_HOTKEY_SHIFT }
        if flags.contains(.command) { modifiers |= RL_HOTKEY_WIN }
        self.modifiers = modifiers
        virtualKey = Int32(key)
        guard setting != nil, macAllows, !takesFromTheSystem else { return nil }
    }

    /// ⌘Q, ⌘W, ⌘H, ⌘Tab, ⌘Space and the like: a global shortcut would take them from every app (the Windows
    /// recorder refuses Windows' own keys the same way).
    var takesFromTheSystem: Bool {
        guard modifiers & RL_HOTKEY_WIN != 0 else { return false }
        if virtualKey == 0x09 || virtualKey == 0x20 { return true } // Tab, Space
        return modifiers == RL_HOTKEY_WIN && [0x51, 0x57, 0x48, 0x4D].contains(virtualKey) // Q, W, H, M
    }

    /// The text the settings file stores.
    var setting: String? {
        Core.readString { rl_hotkey_format(modifiers, virtualKey, $0, $1) }
    }

    /// macOS 15 refuses shortcuts whose only modifiers are Option and Shift (error -9868, Apple forums 763878), so
    /// the Mac also asks for ⌃ or ⌘, except on a function key.
    var macAllows: Bool {
        if modifiers & (RL_HOTKEY_CTRL | RL_HOTKEY_WIN) != 0 { return true }
        return modifiers & RL_HOTKEY_ALT == 0 && KeyCodes.isFunctionKey(Int(virtualKey))
    }

    /// The Mac's way of writing it, e.g. ⌃⌥L.
    var display: String {
        var text = ""
        if modifiers & RL_HOTKEY_CTRL != 0 { text += "⌃" }
        if modifiers & RL_HOTKEY_ALT != 0 { text += "⌥" }
        if modifiers & RL_HOTKEY_SHIFT != 0 { text += "⇧" }
        if modifiers & RL_HOTKEY_WIN != 0 { text += "⌘" }
        return text + KeyCodes.symbol(forVirtualKey: Int(virtualKey))
    }

    var carbonKeyCode: Int? { KeyCodes.carbon(forVirtualKey: Int(virtualKey)) }

    var carbonModifiers: UInt32 {
        var flags: UInt32 = 0
        if modifiers & RL_HOTKEY_CTRL != 0 { flags |= UInt32(controlKey) }
        if modifiers & RL_HOTKEY_ALT != 0 { flags |= UInt32(optionKey) }
        if modifiers & RL_HOTKEY_SHIFT != 0 { flags |= UInt32(shiftKey) }
        if modifiers & RL_HOTKEY_WIN != 0 { flags |= UInt32(cmdKey) }
        return flags
    }

    /// For a menu item's key equivalent: the key and its modifiers, when the key is a letter or digit.
    var menuEquivalent: (key: String, mask: NSEvent.ModifierFlags)? {
        guard (0x30...0x39).contains(virtualKey) || (0x41...0x5A).contains(virtualKey) else { return nil }
        var mask: NSEvent.ModifierFlags = []
        if modifiers & RL_HOTKEY_CTRL != 0 { mask.insert(.control) }
        if modifiers & RL_HOTKEY_ALT != 0 { mask.insert(.option) }
        if modifiers & RL_HOTKEY_SHIFT != 0 { mask.insert(.shift) }
        if modifiers & RL_HOTKEY_WIN != 0 { mask.insert(.command) }
        return (String(UnicodeScalar(UInt8(virtualKey))).lowercased(), mask)
    }
}

/// The global shortcut that toggles the glow (doc 06 §4), registered with Carbon's RegisterEventHotKey, which needs
/// no Accessibility or Input Monitoring permission (10-MACOS §5). Main thread.
@MainActor
final class GlobalHotkey {
    private var handler: EventHandlerRef?
    private var registered: EventHotKeyRef?
    private let pressed: @MainActor () -> Void

    init(pressed: @escaping @MainActor () -> Void) {
        self.pressed = pressed
        var type = EventTypeSpec(eventClass: OSType(kEventClassKeyboard), eventKind: UInt32(kEventHotKeyPressed))
        let context = Unmanaged.passUnretained(self).toOpaque()
        InstallEventHandler(GetApplicationEventTarget(), { _, _, context in
            guard let context else { return noErr }
            let hotkey = Unmanaged<GlobalHotkey>.fromOpaque(context).takeUnretainedValue()
            MainActor.assumeIsolated { hotkey.pressed() } // Carbon calls on the main thread
            return noErr
        }, 1, &type, context, &handler)
    }

    /// Why a shortcut isn't registered: Settings shows the matching warning, as on Windows.
    enum Problem: Equatable {
        /// A shortcut the Mac can't use (from a Windows settings file: Alt+L, Insert, F24…).
        case notOnMac
        /// Another app holds it, or macOS refused it.
        case taken
    }

    /// Registers the shortcut in the setting ("" for none). Nil when it is registered (or there is none).
    @discardableResult
    func register(_ setting: String) -> Problem? {
        if let registered { UnregisterEventHotKey(registered) }
        registered = nil
        guard !setting.isEmpty else { return nil }
        guard let shortcut = Shortcut(setting: setting), shortcut.macAllows,
              let keyCode = shortcut.carbonKeyCode else { return .notOnMac }
        let id = EventHotKeyID(signature: OSType(0x524C_4854), id: 1) // "RLHT"
        var ref: EventHotKeyRef?
        let status = RegisterEventHotKey(UInt32(keyCode), shortcut.carbonModifiers, id, GetApplicationEventTarget(),
                                         0, &ref)
        guard status == noErr, let ref else { return .taken }
        registered = ref
        return nil
    }
}

/// Windows virtual-key codes (what the setting names) and the Mac's Carbon key codes, for a US layout's positions.
enum KeyCodes {
    private static let pairs: [(vk: Int, carbon: Int, symbol: String)] = {
        var list: [(Int, Int, String)] = []
        let letters: [(Character, Int)] = [
            ("A", kVK_ANSI_A), ("B", kVK_ANSI_B), ("C", kVK_ANSI_C), ("D", kVK_ANSI_D), ("E", kVK_ANSI_E),
            ("F", kVK_ANSI_F), ("G", kVK_ANSI_G), ("H", kVK_ANSI_H), ("I", kVK_ANSI_I), ("J", kVK_ANSI_J),
            ("K", kVK_ANSI_K), ("L", kVK_ANSI_L), ("M", kVK_ANSI_M), ("N", kVK_ANSI_N), ("O", kVK_ANSI_O),
            ("P", kVK_ANSI_P), ("Q", kVK_ANSI_Q), ("R", kVK_ANSI_R), ("S", kVK_ANSI_S), ("T", kVK_ANSI_T),
            ("U", kVK_ANSI_U), ("V", kVK_ANSI_V), ("W", kVK_ANSI_W), ("X", kVK_ANSI_X), ("Y", kVK_ANSI_Y),
            ("Z", kVK_ANSI_Z),
            ("0", kVK_ANSI_0), ("1", kVK_ANSI_1), ("2", kVK_ANSI_2), ("3", kVK_ANSI_3), ("4", kVK_ANSI_4),
            ("5", kVK_ANSI_5), ("6", kVK_ANSI_6), ("7", kVK_ANSI_7), ("8", kVK_ANSI_8), ("9", kVK_ANSI_9),
        ]
        for (character, carbon) in letters {
            list.append((Int(character.asciiValue!), carbon, String(character)))
        }
        let functions = [kVK_F1, kVK_F2, kVK_F3, kVK_F4, kVK_F5, kVK_F6, kVK_F7, kVK_F8, kVK_F9, kVK_F10, kVK_F11,
                         kVK_F12, kVK_F13, kVK_F14, kVK_F15, kVK_F16, kVK_F17, kVK_F18, kVK_F19, kVK_F20]
        for (index, carbon) in functions.enumerated() { list.append((0x70 + index, carbon, "F\(index + 1)")) }
        let named: [(Int, Int, String)] = [
            (0x20, kVK_Space, "Space"), (0x0D, kVK_Return, "↩"), (0x09, kVK_Tab, "⇥"), (0x1B, kVK_Escape, "⎋"),
            (0x08, kVK_Delete, "⌫"), (0x2E, kVK_ForwardDelete, "⌦"), (0x24, kVK_Home, "↖"), (0x23, kVK_End, "↘"),
            (0x21, kVK_PageUp, "⇞"), (0x22, kVK_PageDown, "⇟"), (0x25, kVK_LeftArrow, "←"), (0x26, kVK_UpArrow, "↑"),
            (0x27, kVK_RightArrow, "→"), (0x28, kVK_DownArrow, "↓"),
            (0x60, kVK_ANSI_Keypad0, "Num0"), (0x61, kVK_ANSI_Keypad1, "Num1"), (0x62, kVK_ANSI_Keypad2, "Num2"),
            (0x63, kVK_ANSI_Keypad3, "Num3"), (0x64, kVK_ANSI_Keypad4, "Num4"), (0x65, kVK_ANSI_Keypad5, "Num5"),
            (0x66, kVK_ANSI_Keypad6, "Num6"), (0x67, kVK_ANSI_Keypad7, "Num7"), (0x68, kVK_ANSI_Keypad8, "Num8"),
            (0x69, kVK_ANSI_Keypad9, "Num9"), (0x6A, kVK_ANSI_KeypadMultiply, "Num*"),
            (0x6B, kVK_ANSI_KeypadPlus, "Num+"), (0x6D, kVK_ANSI_KeypadMinus, "Num-"),
            (0x6E, kVK_ANSI_KeypadDecimal, "Num."), (0x6F, kVK_ANSI_KeypadDivide, "Num/"),
            (0xBA, kVK_ANSI_Semicolon, ";"), (0xBB, kVK_ANSI_Equal, "="), (0xBC, kVK_ANSI_Comma, ","),
            (0xBD, kVK_ANSI_Minus, "-"), (0xBE, kVK_ANSI_Period, "."), (0xBF, kVK_ANSI_Slash, "/"),
            (0xC0, kVK_ANSI_Grave, "`"), (0xDB, kVK_ANSI_LeftBracket, "["), (0xDC, kVK_ANSI_Backslash, "\\"),
            (0xDD, kVK_ANSI_RightBracket, "]"), (0xDE, kVK_ANSI_Quote, "'"),
            (0x2D, kVK_Help, "Help"),         // Insert: the Help key sits where Insert does
            (0xE2, kVK_ISO_Section, "§"),     // Oem102: the extra key beside Shift on ISO keyboards
        ]
        list += named
        return list.map { (vk: $0.0, carbon: $0.1, symbol: $0.2) }
    }()

    static func carbon(forVirtualKey vk: Int) -> Int? { pairs.first { $0.vk == vk }?.carbon }
    static func virtualKey(forCarbon code: Int) -> Int? { pairs.first { $0.carbon == code }?.vk }
    static func symbol(forVirtualKey vk: Int) -> String { pairs.first { $0.vk == vk }?.symbol ?? "?" }
    static func isFunctionKey(_ vk: Int) -> Bool { (0x70...0x87).contains(vk) }
}
